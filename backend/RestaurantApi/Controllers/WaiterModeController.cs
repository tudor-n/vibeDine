using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using RestaurantApi.Data;
using RestaurantApi.DTOs.Requests;
using RestaurantApi.DTOs.Responses;
using RestaurantApi.Hubs;
using RestaurantApi.Models;
using RestaurantApi.Services;
using RestaurantApi.Tenancy;

namespace RestaurantApi.Controllers;

/// <summary>
/// Waiter Mode: the floor plan, table claims, and waiter-entered orders.
///
/// Not to be confused with <see cref="WaiterController"/>, which handles waiter *signup*
/// links. Every route here is tenant-scoped by the {restaurantId} route value — the
/// middleware has already verified membership and, for Waiter-role callers, that the path
/// is allow-listed. Ownership (is this MY session?) is re-checked per endpoint, because
/// membership alone must not let one waiter close a colleague's table.
/// </summary>
[ApiController]
[Authorize]
[Route("api/restaurants/{restaurantId:guid}/waiter")]
public class WaiterModeController : ControllerBase
{
    // Waiter-entered orders get the same input caps as the anonymous customer cart:
    // a waiter is trusted staff, but a fat-fingered quantity still corrupts the bill
    // and the stock decrement at dispatch.
    private const int MaxOrderItems = 100;
    private const int MaxLineQuantity = 99;
    private const int NotesMaxLength = 200;

    private readonly AppDbContext _db;
    private readonly ITableSessionService _sessions;
    private readonly ITenantAccess _access;
    private readonly IHappyHourService _happyHour;
    private readonly IFlashDealService _flashDeals;
    private readonly IStockService _stockService;
    private readonly IOrderEventWriter _events;
    private readonly IHubContext<CustomerHub> _customerHub;
    private readonly IHubContext<StaffHub> _staffHub;
    private readonly ILogger<WaiterModeController> _logger;

    public WaiterModeController(
        AppDbContext db,
        ITableSessionService sessions,
        ITenantAccess access,
        IHappyHourService happyHour,
        IFlashDealService flashDeals,
        IStockService stockService,
        IOrderEventWriter events,
        IHubContext<CustomerHub> customerHub,
        IHubContext<StaffHub> staffHub,
        ILogger<WaiterModeController> logger)
    {
        _db = db;
        _sessions = sessions;
        _access = access;
        _happyHour = happyHour;
        _flashDeals = flashDeals;
        _stockService = stockService;
        _events = events;
        _customerHub = customerHub;
        _staffHub = staffHub;
        _logger = logger;
    }

    private Guid UserId =>
        Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub")!);

    // ── Floor plan ───────────────────────────────────────────────────────────

    /// <summary>
    /// The whole floor in one round-trip: zones, tables, each table's open session (with
    /// its live bill), whether it has an unanswered help request, and how many orders are
    /// in flight. Assembled from a fixed number of set-based queries — never one per table.
    /// </summary>
    [HttpGet("floor")]
    public async Task<IActionResult> GetFloor(Guid restaurantId)
    {
        var zones = await _db.TableZones
            .AsNoTracking()
            .Where(z => z.DeletedAt == null)
            .OrderBy(z => z.SortOrder)
            .Select(z => new WaiterFloorZoneDto(z.Id, z.Name, z.SortOrder))
            .ToListAsync();

        var tables = await _db.Tables
            .AsNoTracking()
            .Where(t => t.DeletedAt == null)
            .OrderBy(t => t.TableNumber)
            .Select(t => new
            {
                t.Id, t.TableNumber, t.ZoneId, t.Capacity, t.Shape, t.PositionX, t.PositionY,
            })
            .ToListAsync();

        var tableIds = tables.Select(t => t.Id).ToList();

        var sessionsByTable = await _sessions.GetOpenSessionsByTableAsync(tableIds);

        // Uses the (table_id, created_at) index on help_requests.
        var tablesWithHelp = (await _db.HelpRequests
            .Where(h => h.Status == "open" && tableIds.Contains(h.TableId))
            .Select(h => h.TableId)
            .Distinct()
            .ToListAsync()).ToHashSet();

        // Per-table active orders, counted independently of sessions: a table can have
        // live QR orders while nobody has claimed it, and the tile must still show them.
        var activeByTable = (await _db.Orders
                .Where(o => o.DeletedAt == null
                            && tableIds.Contains(o.TableId)
                            && TableSessionService.ActiveOrderStatuses.Contains(o.Status))
                .GroupBy(o => o.TableId)
                .Select(g => new { TableId = g.Key, Count = g.Count() })
                .ToListAsync())
            .ToDictionary(x => x.TableId, x => x.Count);

        // Of those, the ones on no bill at all — what the "Comenzi nepreluate" tab
        // (and its badge) counts. Session-null rather than table-session-open, so a
        // round left in flight by a *closed* session is not re-offered for claiming.
        var unclaimedByTable = (await _db.Orders
                .Where(o => o.DeletedAt == null
                            && o.TableServiceSessionId == null
                            && tableIds.Contains(o.TableId)
                            && TableSessionService.ActiveOrderStatuses.Contains(o.Status))
                .GroupBy(o => o.TableId)
                .Select(g => new { TableId = g.Key, Count = g.Count() })
                .ToListAsync())
            .ToDictionary(x => x.TableId, x => x.Count);

        var floorTables = tables.Select(t => new WaiterFloorTableDto(
            t.Id,
            t.TableNumber,
            t.ZoneId,
            t.Capacity,
            t.Shape,
            t.PositionX,
            t.PositionY,
            sessionsByTable.GetValueOrDefault(t.Id),
            tablesWithHelp.Contains(t.Id),
            activeByTable.GetValueOrDefault(t.Id),
            unclaimedByTable.GetValueOrDefault(t.Id))).ToList();

        return Ok(new WaiterFloorResponse(zones, floorTables));
    }

    /// <summary>
    /// The "Comenzi nepreluate" feed: live rounds (QR submits, in practice — a waiter
    /// round always has a session) on tables no session covers, oldest first. Claiming
    /// the table adopts them onto the new bill (see TableSessionService.ClaimTableAsync),
    /// which is what empties this list.
    /// </summary>
    [HttpGet("unclaimed-orders")]
    public async Task<IActionResult> GetUnclaimedOrders()
    {
        var orders = await _db.Orders
            .AsNoTracking()
            .Where(o => o.DeletedAt == null
                        && o.TableServiceSessionId == null
                        && TableSessionService.ActiveOrderStatuses.Contains(o.Status))
            .Include(o => o.Items)
                .ThenInclude(i => i.Product)
            .OrderBy(o => o.CreatedAt)
            .ToListAsync();

        var tableIds = orders.Select(o => o.TableId).Distinct().ToList();
        var tableNumbers = await _db.Tables
            .Where(t => tableIds.Contains(t.Id) && t.DeletedAt == null)
            .Select(t => new { t.Id, t.TableNumber })
            .ToDictionaryAsync(t => t.Id, t => t.TableNumber);

        // Totalled from the lines, not Order.TotalAmount, matching the cumulative-total
        // rule — the stored header total is not trusted for display.
        var result = orders.Select(o => new WaiterUnclaimedOrderDto(
            o.Id,
            o.OrderNumber,
            o.Status,
            o.Source,
            o.TableId,
            tableNumbers.GetValueOrDefault(o.TableId),
            o.Items.Sum(i => i.Quantity * i.UnitPriceAtPurchase),
            o.CreatedAt,
            o.Items.Select(i => new WaiterOrderItemDto(
                i.Id,
                i.ProductId,
                i.Product?.Name ?? "Produs șters",
                i.Quantity,
                i.UnitPriceAtPurchase,
                i.Notes,
                i.IsFlashDealItem ?? false)).ToList())).ToList();

        return Ok(result);
    }

    // ── Sessions ─────────────────────────────────────────────────────────────

    [HttpPost("tables/{tableId:guid}/claim")]
    public async Task<IActionResult> ClaimTable(Guid restaurantId, Guid tableId)
    {
        var outcome = await _sessions.ClaimTableAsync(restaurantId, tableId, UserId);

        return outcome.Code switch
        {
            TableSessionOutcomeCode.Created =>
                Created($"/api/restaurants/{restaurantId}/waiter/sessions/{outcome.Session!.Id}", outcome.Session),
            TableSessionOutcomeCode.NotFound => NotFound(new { message = outcome.Message }),
            // The body carries the existing session so the console can render
            // "Ocupată — {waiterName}" without a second round-trip.
            TableSessionOutcomeCode.Conflict => Conflict(new { message = outcome.Message, session = outcome.Session }),
            _ => BadRequest(new { message = outcome.Message }),
        };
    }

    [HttpGet("sessions/{sessionId:guid}")]
    public async Task<IActionResult> GetSession(Guid sessionId)
    {
        var session = await _sessions.GetSessionDtoAsync(sessionId);
        if (session is null) return NotFound(new { message = "Sesiunea nu a fost găsită." });

        var orders = await LoadSessionOrdersAsync(sessionId);

        return Ok(new WaiterSessionDetailDto(session, orders, session.CumulativeTotal));
    }

    [HttpGet("my-sessions")]
    public async Task<IActionResult> GetMySessions() =>
        Ok(await _sessions.GetOpenSessionsForUserAsync(UserId));

    [HttpGet("staff")]
    public async Task<IActionResult> GetStaff(Guid restaurantId) =>
        Ok(await _sessions.GetTransferTargetsAsync(restaurantId));

    [HttpPost("sessions/{sessionId:guid}/transfer")]
    public async Task<IActionResult> TransferSession(
        Guid restaurantId, Guid sessionId, [FromBody] TransferSessionRequest request)
    {
        if (request is null || request.ToUserId == Guid.Empty)
            return BadRequest(new { message = "Selectează colegul către care transferi masa." });

        var canManage = await _access.CanManageRestaurantAsync(UserId, restaurantId);
        var outcome = await _sessions.TransferAsync(restaurantId, sessionId, request.ToUserId, UserId, canManage);
        return MapOutcome(outcome);
    }

    [HttpPost("sessions/{sessionId:guid}/close")]
    public async Task<IActionResult> CloseSession(
        Guid restaurantId, Guid sessionId, [FromBody] CloseSessionRequest request)
    {
        if (request is null)
            return BadRequest(new { message = "Datele de plată lipsesc." });

        var canManage = await _access.CanManageRestaurantAsync(UserId, restaurantId);
        var outcome = await _sessions.CloseAsync(
            restaurantId, sessionId, UserId, canManage,
            request.PaymentMethod, request.CashAmount, request.CardAmount);
        return MapOutcome(outcome);
    }

    /// <summary>
    /// Manager-only, and deliberately absent from the waiter allow-list: a Waiter-role
    /// caller is stopped by the middleware before reaching this action. The explicit
    /// can-manage check below covers managers of *other* restaurants and keeps the rule
    /// visible at the endpoint rather than only in the middleware table.
    /// </summary>
    [HttpPost("sessions/{sessionId:guid}/force-release")]
    public async Task<IActionResult> ForceReleaseSession(Guid restaurantId, Guid sessionId)
    {
        if (!await _access.CanManageRestaurantAsync(UserId, restaurantId))
            return Forbid();

        var outcome = await _sessions.ForceReleaseAsync(restaurantId, sessionId, UserId);
        return MapOutcome(outcome);
    }

    // ── Waiter-entered orders ────────────────────────────────────────────────

    /// <summary>
    /// Opens an empty draft on the table's session. Only the session owner (or a manager)
    /// may add to a table someone else is serving.
    /// </summary>
    [HttpPost("tables/{tableId:guid}/orders")]
    public async Task<IActionResult> CreateOrder(Guid restaurantId, Guid tableId)
    {
        var table = await _db.Tables.FirstOrDefaultAsync(t => t.Id == tableId && t.DeletedAt == null);
        if (table is null) return NotFound(new { message = "Masa nu a fost găsită." });

        var session = await _db.TableServiceSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.TableId == tableId && s.Status == "open");

        if (session is null)
            return Conflict(new { message = "Masa nu este preluată. Preia masa înainte de a comanda." });

        if (session.WaiterUserId != UserId && !await _access.CanManageRestaurantAsync(UserId, restaurantId))
            return Conflict(new { message = $"Masa este preluată de {session.WaiterName}." });

        var order = new Order
        {
            TableId = tableId,
            Status = "draft",
            PaymentStatus = "unpaid",
            TotalAmount = 0m,
            // A waiter order has no diner phone behind it: no GuestId, no DeviceSessionId,
            // so it never appears in the party panel or a device session's running total.
            GuestId = null,
            Source = "waiter",
            PlacedByUserId = UserId,
            TableServiceSessionId = session.Id,
            // Ownership follows the table: the round belongs to the session's waiter
            // (not necessarily the typist — a manager can enter orders on any table),
            // so it lands directly on that waiter's "Comenzi" board.
            ClaimedByWaiterId = session.WaiterUserId ?? UserId,
            ClaimedAt = DateTime.UtcNow,
        };

        _db.Orders.Add(order);
        await _db.SaveChangesAsync();

        return Created($"/api/restaurants/{restaurantId}/waiter/orders/{order.Id}", ToOrderDto(order, []));
    }

    /// <summary>
    /// Full-replace cart sync, mirroring the customer PATCH: a product missing from the
    /// list is removed. Draft-only — once the round is with the kitchen, edits would
    /// desync stock and the ticket the cooks are already reading.
    /// </summary>
    [HttpPatch("orders/{orderId:guid}/items")]
    public async Task<IActionResult> UpdateOrderItems(
        Guid restaurantId, Guid orderId, [FromBody] UpdateWaiterOrderItemsRequest request)
    {
        if (request?.Items is null)
            return BadRequest(new { message = "Lista de produse lipsește." });

        if (request.Items.Count > MaxOrderItems)
            return BadRequest(new { message = $"O comandă poate conține cel mult {MaxOrderItems} produse." });

        if (request.Items.Any(i => i.Quantity < 1 || i.Quantity > MaxLineQuantity))
            return BadRequest(new { message = $"Cantitatea fiecărui produs trebuie să fie între 1 și {MaxLineQuantity}." });

        if (request.Items.Any(i => i.Notes is { Length: > NotesMaxLength }))
            return BadRequest(new { message = $"Observațiile pot avea cel mult {NotesMaxLength} de caractere." });

        var order = await _db.Orders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == orderId && o.DeletedAt == null);

        if (order is null) return NotFound(new { message = "Comanda nu a fost găsită." });

        var guard = await AuthorizeOrderAsync(restaurantId, order);
        if (guard is not null) return guard;

        if (order.Status != "draft")
            return Conflict(new { message = $"Comanda nu mai poate fi modificată (stare: {order.Status})." });

        var productIds = request.Items.Select(i => i.ProductId).Distinct().ToList();
        var products = await _db.Products
            .Where(p => productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id);

        // Any() rather than FirstOrDefault(): an all-zeros product id is "not found" too,
        // and comparing the sentinel against Guid.Empty would wave it through into the
        // indexer below.
        if (productIds.Any(id => !products.ContainsKey(id)))
            return BadRequest(new { message = "Unul dintre produsele selectate nu există." });

        var unavailable = products.Values.FirstOrDefault(p => p.IsAvailable == false);
        if (unavailable is not null)
            return BadRequest(new { message = $"Produsul „{unavailable.Name}” nu este disponibil." });

        // The server prices the bill, never the client. Happy hour re-applies on every
        // sync so a discount that expires mid-order is reflected before submit.
        var happyHour = await _happyHour.GetActiveDealAsync(order.RestaurantId);

        var incomingIds = request.Items.Select(i => i.ProductId).ToHashSet();
        foreach (var stale in order.Items.Where(i => !incomingIds.Contains(i.ProductId) && i.IsFlashDealItem != true).ToList())
            _db.OrderItems.Remove(stale);

        // A flash-deal line was claimed by the diner, not composed here — it survives every
        // sync untouched and is never in the incoming list.
        var surviving = order.Items.Where(i => i.IsFlashDealItem == true).ToList();

        foreach (var line in request.Items)
        {
            var product = products[line.ProductId];
            var notes = string.IsNullOrWhiteSpace(line.Notes) ? null : line.Notes.Trim();
            var existing = order.Items.FirstOrDefault(i => i.ProductId == line.ProductId && i.IsFlashDealItem != true);

            if (existing is not null)
            {
                existing.Quantity = line.Quantity;
                existing.UnitPriceAtPurchase = _happyHour.EffectiveUnitPrice(product, happyHour);
                existing.Notes = notes;
                surviving.Add(existing);
            }
            else
            {
                var item = new OrderItem
                {
                    OrderId = order.Id,
                    ProductId = product.Id,
                    Quantity = line.Quantity,
                    UnitPriceAtPurchase = _happyHour.EffectiveUnitPrice(product, happyHour),
                    IsFlashDealItem = false,
                    WasAiRecommended = false,
                    Notes = notes,
                };
                // Add to the set only — EF's relationship fixup puts it into order.Items for
                // us, and adding it there by hand would list (and bill) the line twice.
                _db.OrderItems.Add(item);
                surviving.Add(item);
            }
        }

        // Totalled from the lines this sync decided on, not from order.Items: that collection
        // still holds the rows queued for deletion until SaveChanges runs.
        order.TotalAmount = surviving.Sum(i => i.Quantity * i.UnitPriceAtPurchase);
        await _db.SaveChangesAsync();

        return Ok(ToOrderDto(order, BuildItemDtos(surviving, products)));
    }

    /// <summary>
    /// Sends the round to the kitchen. Unlike the diner flow this dispatches immediately:
    /// the 2-minute flash-deal hold exists to upsell a phone that is still on the order
    /// screen, and there is no phone here — so no EvaluateEligibilityAsync. Stock still
    /// decrements exactly once, through the same idempotent dispatch path.
    /// </summary>
    [HttpPost("orders/{orderId:guid}/submit")]
    public async Task<IActionResult> SubmitOrder(Guid restaurantId, Guid orderId)
    {
        var order = await _db.Orders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == orderId && o.Status == "draft" && o.DeletedAt == null);

        if (order is null)
            return NotFound(new { message = "Comanda nu a fost găsită sau a fost deja trimisă." });

        var guard = await AuthorizeOrderAsync(restaurantId, order);
        if (guard is not null) return guard;

        if (order.Items.Count == 0)
            return BadRequest(new { message = "Comanda este goală. Adaugă produse înainte de a trimite." });

        order.TotalAmount = order.Items.Sum(i => i.Quantity * i.UnitPriceAtPurchase);
        order.Status = "pending_dispatch";

        // Same per-restaurant sequence the customer submit uses, so waiter and QR rounds
        // share one numbering the kitchen can read off a single board.
        var lastNumber = await _db.Orders
            .IgnoreQueryFilters()
            .Where(o => o.RestaurantId == order.RestaurantId && o.OrderNumber != null)
            .MaxAsync(o => (int?)o.OrderNumber) ?? 0;
        order.OrderNumber = lastNumber + 1;

        await _db.SaveChangesAsync();

        await _flashDeals.DispatchToKitchenAsync(order.Id);

        // Re-read: DispatchToKitchenAsync flipped the status (and the stock flag) through
        // the same scoped context.
        return Ok(new WaiterSubmitOrderResponse(order.Id, order.OrderNumber, order.Status, order.TotalAmount));
    }

    /// <summary>
    /// Cancels a waiter-visible order. A draft never reached the kitchen and never touched
    /// stock, so it is hard-deleted with no event; anything further along is soft-deleted,
    /// restores its stock (idempotently) and announces itself on the outbox.
    /// </summary>
    [HttpPost("orders/{orderId:guid}/cancel")]
    public async Task<IActionResult> CancelOrder(Guid restaurantId, Guid orderId)
    {
        var order = await _db.Orders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == orderId && o.DeletedAt == null);

        if (order is null) return NotFound(new { message = "Comanda nu a fost găsită." });

        var guard = await AuthorizeOrderAsync(restaurantId, order);
        if (guard is not null) return guard;

        if (order.Status == "draft")
        {
            _db.OrderItems.RemoveRange(order.Items);
            _db.Orders.Remove(order);
            await _db.SaveChangesAsync();
            return Ok(new WaiterCancelOrderResponse(order.Id, "cancelled"));
        }

        if (!TableSessionService.ActiveOrderStatuses.Contains(order.Status))
            return Conflict(new { message = $"Comanda nu mai poate fi anulată (stare: {order.Status})." });

        order.Status = "cancelled";
        order.DeletedAt = DateTime.UtcNow;

        _events.Enqueue(
            _db,
            OrderEventTypes.OrderCancelled,
            order.RestaurantId,
            order.Id,
            await OrderEventPayloads.BuildOrderPayloadAsync(_db, order));

        await _db.SaveChangesAsync();

        await _stockService.ApplyRestoreForOrderAsync(order);

        await _customerHub.Clients
            .Group(TenantGroups.Table(order.RestaurantId, order.TableId))
            .SendAsync("OrderStatusUpdated", new { orderId = order.Id, status = order.Status });

        var cancelPayload = new { orderId = order.Id, orderNumber = order.OrderNumber, status = order.Status };
        await _staffHub.Clients
            .Group(TenantGroups.Role(order.RestaurantId, "admin"))
            .SendAsync("OrderStatusUpdated", cancelPayload);
        await _staffHub.Clients
            .Group(TenantGroups.Role(order.RestaurantId, "WaitStaff"))
            .SendAsync("OrderStatusUpdated", cancelPayload);

        _logger.LogInformation("Waiter {UserId} cancelled order {OrderId} (#{Number})", UserId, order.Id, order.OrderNumber);
        return Ok(new WaiterCancelOrderResponse(order.Id, order.Status));
    }

    // ── Masa auxiliară ───────────────────────────────────────────────────────
    // A private, per-waiter holding area for lines pulled off live rounds (a
    // problem with a dish, guests switching tables, …). Not a physical table:
    // no floor tile, no QR, no session. Money follows the line (price snapshot);
    // stock never moves in either direction — the food already exists.

    /// <summary>Lines already with the kitchen — the only ones worth parking.</summary>
    private static readonly string[] AuxMovableStatuses = ["submitted", "preparing", "ready"];

    [HttpGet("aux-table")]
    public async Task<IActionResult> GetAuxTable()
    {
        var items = await _db.AuxTableItems
            .AsNoTracking()
            .Where(a => a.WaiterUserId == UserId)
            .OrderBy(a => a.CreatedAt)
            .Select(a => new WaiterAuxItemDto(
                a.Id, a.ProductId, a.ProductName, a.Quantity, a.UnitPriceAtPurchase,
                a.Notes, a.SourceOrderNumber, a.SourceTableNumber, a.CreatedAt))
            .ToListAsync();

        return Ok(new WaiterAuxTableResponse(items, items.Sum(i => i.Quantity * i.UnitPriceAtPurchase)));
    }

    /// <summary>
    /// Pulls (part of) a line off a live round onto the caller's aux table. The
    /// source order's total shrinks with it; an order left with no lines is
    /// auto-cancelled — with NO stock restore, because the food moved, it didn't
    /// come back.
    /// </summary>
    [HttpPost("orders/{orderId:guid}/items/{itemId:guid}/move-to-aux")]
    public async Task<IActionResult> MoveItemToAux(
        Guid restaurantId, Guid orderId, Guid itemId, [FromBody] MoveItemToAuxRequest? request)
    {
        var order = await _db.Orders
            .Include(o => o.Items).ThenInclude(i => i.Product)
            .Include(o => o.Table)
            .FirstOrDefaultAsync(o => o.Id == orderId && o.DeletedAt == null);
        if (order is null) return NotFound(new { message = "Comanda nu a fost găsită." });

        var guard = await AuthorizeOrderAsync(restaurantId, order);
        if (guard is not null) return guard;

        if (!AuxMovableStatuses.Contains(order.Status))
            return Conflict(new { message = $"Doar comenzile aflate la bucătărie pot fi mutate (stare: {order.Status})." });

        var item = order.Items.FirstOrDefault(i => i.Id == itemId);
        if (item is null) return NotFound(new { message = "Produsul nu a fost găsit în comandă." });

        var quantity = request?.Quantity ?? item.Quantity;
        if (quantity < 1 || quantity > item.Quantity)
            return BadRequest(new { message = $"Cantitatea trebuie să fie între 1 și {item.Quantity}." });

        _db.AuxTableItems.Add(new AuxTableItem
        {
            WaiterUserId = UserId,
            ProductId = item.ProductId,
            ProductName = item.Product?.Name ?? "Produs șters",
            Quantity = quantity,
            UnitPriceAtPurchase = item.UnitPriceAtPurchase,
            Notes = item.Notes,
            SourceOrderId = order.Id,
            SourceOrderNumber = order.OrderNumber,
            SourceTableNumber = order.Table?.TableNumber,
        });

        var movedAll = quantity == item.Quantity;
        if (movedAll) _db.OrderItems.Remove(item);
        else item.Quantity -= quantity;

        // Partial: item.Quantity is already decremented. Full: exclude the removed row.
        order.TotalAmount = order.Items
            .Where(i => !(movedAll && i.Id == item.Id))
            .Sum(i => i.Quantity * i.UnitPriceAtPurchase);

        var emptied = movedAll && order.Items.All(i => i.Id == item.Id);

        // Two saves, one transaction: the outbox payload builder reads items from
        // the DB, so the removal must be flushed first for the cancel event to
        // carry the true (line-less) remainder rather than the moved food.
        await using var tx = await _db.Database.BeginTransactionAsync();
        await _db.SaveChangesAsync();

        if (emptied)
        {
            order.Status = "cancelled";
            order.DeletedAt = DateTime.UtcNow;
            _events.Enqueue(
                _db,
                OrderEventTypes.OrderCancelled,
                order.RestaurantId,
                order.Id,
                await OrderEventPayloads.BuildOrderPayloadAsync(_db, order));
            await _db.SaveChangesAsync();
        }
        await tx.CommitAsync();

        if (emptied)
        {
            await _customerHub.Clients
                .Group(TenantGroups.Table(order.RestaurantId, order.TableId))
                .SendAsync("OrderStatusUpdated", new { orderId = order.Id, status = order.Status });
        }
        await BroadcastOrderChangedAsync(order);

        _logger.LogInformation(
            "Waiter {UserId} moved {Quantity}× item {ItemId} from order {OrderId} to aux (emptied: {Emptied})",
            UserId, quantity, itemId, order.Id, emptied);

        return Ok(new { orderId = order.Id, orderStatus = order.Status, movedQuantity = quantity });
    }

    /// <summary>
    /// Lands an aux line on one of the caller's tables as an archived, completed
    /// round: it joins that session's bill (cumulative total counts completed,
    /// soft-deleted rounds) but never reaches the kitchen board, and
    /// StockDecremented=true keeps stock untouched — the food was already cooked
    /// and accounted at its source table's dispatch.
    /// </summary>
    [HttpPost("aux-table/items/{auxItemId:guid}/move-to-table")]
    public async Task<IActionResult> MoveAuxItemToTable(
        Guid restaurantId, Guid auxItemId, [FromBody] MoveAuxItemToTableRequest? request)
    {
        if (request is null || request.TableId == Guid.Empty)
            return BadRequest(new { message = "Selectează masa pe care muți produsul." });

        var aux = await _db.AuxTableItems
            .FirstOrDefaultAsync(a => a.Id == auxItemId && a.WaiterUserId == UserId);
        if (aux is null) return NotFound(new { message = "Produsul nu a fost găsit pe masa ta auxiliară." });

        var table = await _db.Tables
            .FirstOrDefaultAsync(t => t.Id == request.TableId && t.DeletedAt == null);
        if (table is null) return NotFound(new { message = "Masa nu a fost găsită." });

        var session = await _db.TableServiceSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.TableId == table.Id && s.Status == "open");
        if (session is null)
            return Conflict(new { message = "Masa nu este preluată. Preia masa înainte de a muta produsul." });
        if (session.WaiterUserId != UserId && !await _access.CanManageRestaurantAsync(UserId, restaurantId))
            return Conflict(new { message = $"Masa este preluată de {session.WaiterName}." });

        var lastNumber = await _db.Orders
            .IgnoreQueryFilters()
            .Where(o => o.RestaurantId == restaurantId && o.OrderNumber != null)
            .MaxAsync(o => (int?)o.OrderNumber) ?? 0;

        var now = DateTime.UtcNow;
        var order = new Order
        {
            TableId = table.Id,
            Status = "completed",
            PaymentStatus = "unpaid",
            Source = "waiter",
            PlacedByUserId = UserId,
            TableServiceSessionId = session.Id,
            ClaimedByWaiterId = session.WaiterUserId ?? UserId,
            ClaimedAt = now,
            OrderNumber = lastNumber + 1,
            TotalAmount = aux.Quantity * aux.UnitPriceAtPurchase,
            ReadyAt = now,
            DeletedAt = now,
            StockDecremented = true,
            Items =
            {
                new OrderItem
                {
                    ProductId = aux.ProductId,
                    Quantity = aux.Quantity,
                    UnitPriceAtPurchase = aux.UnitPriceAtPurchase,
                    Notes = aux.Notes,
                    IsFlashDealItem = false,
                    WasAiRecommended = false,
                },
            },
        };
        _db.Orders.Add(order);
        _db.AuxTableItems.Remove(aux);

        // Revenue books at completion; the source order's total already shrank when
        // the line left it, so the money is counted exactly once.
        _events.Enqueue(
            _db,
            OrderEventTypes.OrderCompleted,
            restaurantId,
            order.Id,
            OrderEventPayloads.BuildOrderCompletedPayload(order));

        await _db.SaveChangesAsync();
        await BroadcastOrderChangedAsync(order);

        _logger.LogInformation(
            "Waiter {UserId} moved aux item {AuxItemId} to table {TableId} as order {OrderId}",
            UserId, auxItemId, table.Id, order.Id);

        return Ok(new { orderId = order.Id, orderNumber = order.OrderNumber, tableNumber = table.TableNumber });
    }

    /// <summary>Writes the parked line off. Stock stays consumed — nothing returns to shelf.</summary>
    [HttpDelete("aux-table/items/{auxItemId:guid}")]
    public async Task<IActionResult> DiscardAuxItem(Guid auxItemId)
    {
        var aux = await _db.AuxTableItems
            .FirstOrDefaultAsync(a => a.Id == auxItemId && a.WaiterUserId == UserId);
        if (aux is null) return NotFound(new { message = "Produsul nu a fost găsit pe masa ta auxiliară." });

        _db.AuxTableItems.Remove(aux);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Waiter {UserId} discarded aux item {AuxItemId} ({Name} ×{Qty})",
            UserId, auxItemId, aux.ProductName, aux.Quantity);

        return Ok(new { id = auxItemId });
    }

    /// <summary>Nudges every staff surface (boards, floors, table views) to refetch.</summary>
    private async Task BroadcastOrderChangedAsync(Order order)
    {
        var payload = new { orderId = order.Id, orderNumber = order.OrderNumber, status = order.Status };
        await _staffHub.Clients
            .Group(TenantGroups.Role(order.RestaurantId, "admin"))
            .SendAsync("OrderStatusUpdated", payload);
        await _staffHub.Clients
            .Group(TenantGroups.Role(order.RestaurantId, "WaitStaff"))
            .SendAsync("OrderStatusUpdated", payload);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns a non-null error result when the caller may not touch this order: it must
    /// belong to an open session they own, unless they are a manager. Orders with no
    /// session (plain QR rounds at an unclaimed table) are not the waiter console's to edit.
    /// </summary>
    private async Task<IActionResult?> AuthorizeOrderAsync(Guid restaurantId, Order order)
    {
        if (await _access.CanManageRestaurantAsync(UserId, restaurantId)) return null;

        if (order.TableServiceSessionId is not Guid sessionId)
            return Conflict(new { message = "Comanda nu aparține unei mese preluate." });

        var owner = await _db.TableServiceSessions
            .AsNoTracking()
            .Where(s => s.Id == sessionId)
            .Select(s => new { s.WaiterUserId, s.WaiterName, s.Status })
            .FirstOrDefaultAsync();

        if (owner is null)
            return Conflict(new { message = "Sesiunea comenzii nu mai există." });

        if (owner.WaiterUserId != UserId)
            return StatusCode(StatusCodes.Status403Forbidden,
                new { message = $"Masa este preluată de {owner.WaiterName}." });

        return null;
    }

    private async Task<IReadOnlyList<WaiterOrderDto>> LoadSessionOrdersAsync(Guid sessionId)
    {
        // DeletedAt is not filtered: a round archived by OrderFinalizationService is
        // soft-deleted but still on the bill (same rule as cumulativeTotal).
        var orders = await _db.Orders
            .AsNoTracking()
            .Where(o => o.TableServiceSessionId == sessionId && o.Status != "merged")
            .Include(o => o.Items)
                .ThenInclude(i => i.Product)
            .OrderBy(o => o.CreatedAt)
            .ToListAsync();

        return orders.Select(o => new WaiterOrderDto(
            o.Id,
            o.OrderNumber,
            o.Status,
            o.Source,
            o.PlacedByUserId,
            o.TotalAmount,
            o.CreatedAt,
            o.Items.Select(i => new WaiterOrderItemDto(
                i.Id,
                i.ProductId,
                i.Product?.Name ?? "Produs șters",
                i.Quantity,
                i.UnitPriceAtPurchase,
                i.Notes,
                i.IsFlashDealItem ?? false)).ToList())).ToList();
    }

    private static WaiterOrderDto ToOrderDto(Order order, IReadOnlyList<WaiterOrderItemDto> items) =>
        new(order.Id, order.OrderNumber, order.Status, order.Source, order.PlacedByUserId,
            order.TotalAmount, order.CreatedAt, items);

    private static List<WaiterOrderItemDto> BuildItemDtos(
        IEnumerable<OrderItem> items, IReadOnlyDictionary<Guid, Product> products) =>
        items.Select(i => new WaiterOrderItemDto(
            i.Id,
            i.ProductId,
            products.TryGetValue(i.ProductId, out var p) ? p.Name : i.Product?.Name ?? "Produs șters",
            i.Quantity,
            i.UnitPriceAtPurchase,
            i.Notes,
            i.IsFlashDealItem ?? false)).ToList();

    private IActionResult MapOutcome(TableSessionOutcome outcome) => outcome.Code switch
    {
        TableSessionOutcomeCode.Ok => Ok(outcome.Session),
        TableSessionOutcomeCode.NotFound => NotFound(new { message = outcome.Message }),
        TableSessionOutcomeCode.Conflict => Conflict(new { message = outcome.Message, session = outcome.Session }),
        TableSessionOutcomeCode.Forbidden =>
            StatusCode(StatusCodes.Status403Forbidden, new { message = outcome.Message }),
        _ => BadRequest(new { message = outcome.Message }),
    };
}
