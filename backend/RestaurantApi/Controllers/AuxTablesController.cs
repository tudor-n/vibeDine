using System.Security.Claims;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using RestaurantApi.Data;
using RestaurantApi.Hubs;
using RestaurantApi.Models;
using RestaurantApi.Tenancy;

namespace RestaurantApi.Controllers;

/// <summary>
/// Auxiliary ("parking") tables — one per waiter, off the floor plan, no QR.
/// When something goes wrong with an order (dropped food, out-of-stock item),
/// staff park the whole order or a selection of its items on a waiter's aux
/// table. Managers get a console over every aux table and are the only ones who
/// can send a parked order back to a real table or hand it to another waiter.
/// </summary>
[ApiController]
[Authorize]
[Route("api/restaurants/{restaurantId:guid}/admin")]
public class AuxTablesController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IHubContext<StaffHub> _staffHub;
    private readonly ITenantAccess _access;
    private readonly ILogger<AuxTablesController> _logger;

    private Guid UserId =>
        Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub")!);

    // Active-board statuses (the three kanban columns). Only orders in these
    // states can be parked / unparked — anything else is terminal.
    private static readonly HashSet<string> ActiveStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "submitted", "preparing", "ready"
    };

    public AuxTablesController(
        AppDbContext db,
        IHubContext<StaffHub> staffHub,
        ITenantAccess access,
        ILogger<AuxTablesController> logger)
    {
        _db = db;
        _staffHub = staffHub;
        _access = access;
        _logger = logger;
    }

    // ── Park an order (waiters may park their own; managers may park any) ────

    [HttpPost("orders/{orderId:guid}/move-to-aux")]
    public async Task<IActionResult> MoveOrderToAux(
        Guid restaurantId,
        Guid orderId,
        [FromBody] MoveOrderToAuxRequest request)
    {
        var order = await _db.Orders
            .Include(o => o.Items)
            .Include(o => o.Table)
            .FirstOrDefaultAsync(o => o.Id == orderId && o.DeletedAt == null);

        if (order == null)
            return NotFound(new { message = "Comanda nu a fost găsită sau a fost deja finalizată." });

        if (!ActiveStatuses.Contains(order.Status))
            return Conflict(new { message = $"Comanda nu mai e activă (stare curentă: {order.Status})." });

        if (order.Table.IsAux)
            return Conflict(new { message = "Comanda este deja pe o masă auxiliară." });

        // Managers may park any order onto any waiter's aux table. A waiter may
        // only park an order they personally claimed, and only onto their own.
        var canManage = await _access.CanManageRestaurantAsync(UserId, restaurantId);
        if (!canManage)
        {
            if (order.ClaimedByWaiterId != UserId)
                return StatusCode(StatusCodes.Status403Forbidden,
                    new { message = "Poți muta doar comenzile preluate de tine." });

            if (request?.TargetWaiterId is Guid explicitTarget && explicitTarget != UserId)
                return StatusCode(StatusCodes.Status403Forbidden,
                    new { message = "Doar managerul poate muta comanda pe masa auxiliară a altui coleg." });
        }

        // Aux tables belong to waiters only — a manager parking an order must
        // pick which waiter's table receives it (managers own no aux table).
        var targetWaiterId = request?.TargetWaiterId ?? UserId;
        if (!await _access.IsActiveWaiterAsync(targetWaiterId, restaurantId))
            return BadRequest(new { message = "Selectează un ospătar valid pentru masa auxiliară." });

        var waiterName = await _db.Users
            .Where(u => u.Id == targetWaiterId)
            .Select(u => u.FullName)
            .FirstOrDefaultAsync() ?? "Ospătar";

        // Captured before any mutation — the broadcast reports where the order came from.
        var sourceTable = order.Table;

        var aux = await GetOrCreateAuxTableAsync(targetWaiterId, waiterName);

        // Null items = whole move. A selection that covers every item at full
        // quantity collapses to a whole move too (no pointless order split).
        var wholeMove = request?.Items == null;
        Dictionary<Guid, int>? movedQuantities = null;

        if (request?.Items != null)
        {
            if (request.Items.Count == 0)
                return BadRequest(new { message = "Selectează cel puțin un produs." });

            var itemsById = order.Items.ToDictionary(i => i.Id);
            movedQuantities = new Dictionary<Guid, int>();
            foreach (var sel in request.Items)
            {
                if (movedQuantities.ContainsKey(sel.OrderItemId)
                    || !itemsById.TryGetValue(sel.OrderItemId, out var item)
                    || sel.Quantity < 1
                    || sel.Quantity > item.Quantity)
                    return BadRequest(new { message = "Selecția de produse nu este validă." });

                movedQuantities[sel.OrderItemId] = sel.Quantity;
            }

            if (movedQuantities.Count == order.Items.Count
                && order.Items.All(i => movedQuantities.TryGetValue(i.Id, out var q) && q == i.Quantity))
            {
                wholeMove = true;
                movedQuantities = null;
            }
        }

        var now = DateTime.UtcNow;
        Order movedOrder;

        if (wholeMove)
        {
            order.MovedFromTableId = order.TableId;
            order.TableId = aux.Id;
            order.MovedToAuxAt = now;
            // Parking pauses the 45s auto-archive countdown of a "ready" order.
            order.ReadyAt = null;
            order.ClaimedByWaiterId = targetWaiterId;
            order.ClaimedAt = now;
            await _db.SaveChangesAsync();
            movedOrder = order;
        }
        else
        {
            // Split: the selected quantities leave on a NEW order parked on the
            // aux table; the source order keeps the rest at its original table.
            // Per-restaurant sequence, same race-tolerated pattern as SubmitOrder.
            var lastNumber = await _db.Orders
                .IgnoreQueryFilters()
                .Where(o => o.RestaurantId == order.RestaurantId && o.OrderNumber != null)
                .MaxAsync(o => (int?)o.OrderNumber) ?? 0;

            var newOrder = new Order
            {
                Id = Guid.NewGuid(),
                RestaurantId = order.RestaurantId,
                TableId = aux.Id,
                GuestId = null,
                Status = order.Status,
                PaymentStatus = order.PaymentStatus,
                // Inheriting the flag keeps the existing cancel→stock-restore path
                // correct for both halves: restore recomputes from each order's
                // own items, so each half puts back exactly what it holds.
                StockDecremented = order.StockDecremented,
                CreatedAt = now,
                OrderNumber = lastNumber + 1,
                ReadyAt = null,
                ClaimedByWaiterId = targetWaiterId,
                ClaimedAt = now,
                MovedFromTableId = order.TableId,
                MovedToAuxAt = now,
            };
            _db.Orders.Add(newOrder);

            // Recompute both totals from items — never subtract from the stored
            // total — before any quantity is mutated below.
            decimal movedTotal = 0m, sourceTotal = 0m;
            foreach (var item in order.Items)
            {
                var moved = movedQuantities!.GetValueOrDefault(item.Id);
                movedTotal += moved * item.UnitPriceAtPurchase;
                sourceTotal += (item.Quantity - moved) * item.UnitPriceAtPurchase;
            }
            order.TotalAmount = sourceTotal;
            newOrder.TotalAmount = movedTotal;

            foreach (var (orderItemId, movedQty) in movedQuantities!)
            {
                var item = order.Items.First(i => i.Id == orderItemId);
                if (movedQty == item.Quantity)
                {
                    // Full quantity → re-parent the row onto the new order.
                    item.OrderId = newOrder.Id;
                }
                else
                {
                    // Partial quantity → shrink the source line and clone the
                    // moved share onto the new order.
                    item.Quantity -= movedQty;
                    _db.OrderItems.Add(new OrderItem
                    {
                        Id = Guid.NewGuid(),
                        RestaurantId = order.RestaurantId,
                        OrderId = newOrder.Id,
                        ProductId = item.ProductId,
                        Quantity = movedQty,
                        UnitPriceAtPurchase = item.UnitPriceAtPurchase,
                        WasAiRecommended = item.WasAiRecommended,
                        IsFlashDealItem = item.IsFlashDealItem,
                    });
                }
            }

            await _db.SaveChangesAsync();
            movedOrder = newOrder;
        }

        await BroadcastOrderMoved(order.RestaurantId, new
        {
            orderId = movedOrder.Id,
            orderNumber = movedOrder.OrderNumber,
            tableId = aux.Id,
            tableNumber = aux.TableNumber,
            isAux = true,
            auxWaiterId = (Guid?)targetWaiterId,
            auxWaiterName = (string?)waiterName,
            movedFromTableId = movedOrder.MovedFromTableId,
            movedFromTableNumber = sourceTable.TableNumber,
            sourceOrderId = order.Id,
        });

        _logger.LogInformation(
            "Order {OrderId} (#{Number}) parked on aux table {AuxTableId} of waiter {WaiterId} (whole={Whole})",
            movedOrder.Id, movedOrder.OrderNumber, aux.Id, targetWaiterId, wholeMove);

        return Ok(new
        {
            movedOrderId = movedOrder.Id,
            movedOrderNumber = movedOrder.OrderNumber,
            auxTableId = aux.Id,
            auxTableNumber = aux.TableNumber,
            auxWaiterId = targetWaiterId,
            auxWaiterName = waiterName,
            sourceOrderId = order.Id,
            wholeOrderMoved = wholeMove,
        });
    }

    // ── Manager console: every waiter's aux table with its parked orders ─────

    [HttpGet("aux-tables")]
    public async Task<IActionResult> GetAuxTables(Guid restaurantId)
    {
        if (!await _access.CanManageRestaurantAsync(UserId, restaurantId))
            return Forbid();

        // Roster: every active waiter, aux table or not — the console shows a
        // row per colleague so the manager can reassign to any of them.
        var roster = await _db.Memberships
            .Where(m => m.RestaurantId == restaurantId
                        && m.Role == MembershipRole.Waiter
                        && m.Status == MembershipStatus.Active)
            .Join(_db.Users, m => m.UserId, u => u.Id, (m, u) => new
            {
                WaiterId = u.Id,
                u.FullName,
                u.AvatarPath,
            })
            .ToListAsync();

        var auxTables = await _db.Tables
            .Where(t => t.IsAux && t.DeletedAt == null)
            .ToListAsync();
        var auxIds = auxTables.Select(t => t.Id).ToList();

        var parked = await _db.Orders
            .Where(o => o.DeletedAt == null
                        && (o.Status == "submitted" || o.Status == "preparing" || o.Status == "ready")
                        && auxIds.Contains(o.TableId))
            .Include(o => o.Items)
                .ThenInclude(i => i.Product)
            .Include(o => o.ClaimedByWaiter)
            .ToListAsync();

        // Display-only origin lookup — deliberately includes soft-deleted tables
        // so a retired source table still shows its number.
        var movedFromIds = parked
            .Where(o => o.MovedFromTableId != null)
            .Select(o => o.MovedFromTableId!.Value)
            .Distinct()
            .ToList();
        var movedFromNumbers = await _db.Tables
            .Where(t => movedFromIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => t.TableNumber);

        var ordersByTable = parked
            .GroupBy(o => o.TableId)
            .ToDictionary(g => g.Key, g => g.OrderBy(o => o.MovedToAuxAt).ToList());

        object MapOrder(Order o) => new
        {
            id = o.Id,
            orderNumber = o.OrderNumber,
            status = o.Status,
            totalAmount = o.TotalAmount,
            createdAt = o.CreatedAt,
            movedToAuxAt = o.MovedToAuxAt,
            movedFromTableId = o.MovedFromTableId,
            movedFromTableNumber = o.MovedFromTableId is Guid fromId
                ? movedFromNumbers.GetValueOrDefault(fromId)
                : null,
            claimedByWaiterId = o.ClaimedByWaiterId,
            claimedByName = o.ClaimedByWaiter != null ? o.ClaimedByWaiter.FullName : null,
            items = o.Items.Select(i => new
            {
                id = i.Id,
                productName = i.Product != null ? i.Product.Name : "Produs șters",
                quantity = i.Quantity,
                unitPrice = i.UnitPriceAtPurchase,
                subtotal = i.Quantity * i.UnitPriceAtPurchase,
            }).ToList(),
        };

        var auxByWaiter = auxTables
            .Where(t => t.AssignedWaiterId != null)
            .ToDictionary(t => t.AssignedWaiterId!.Value);

        var rows = new List<object>();
        foreach (var waiter in roster)
        {
            auxByWaiter.TryGetValue(waiter.WaiterId, out var aux);
            var orders = aux != null && ordersByTable.TryGetValue(aux.Id, out var list)
                ? list
                : new List<Order>();

            rows.Add(new
            {
                waiterId = (Guid?)waiter.WaiterId,
                waiterName = waiter.FullName,
                avatarUrl = waiter.AvatarPath == null ? null : "/" + waiter.AvatarPath,
                isActiveStaff = true,
                auxTableId = (Guid?)aux?.Id,
                auxTableNumber = aux?.TableNumber,
                orders = orders.Select(MapOrder).ToList(),
            });
        }

        // Orphan aux tables (owner removed from the roster, or user deleted)
        // that still hold parked orders stay visible so the manager can rescue
        // those orders; empty orphans are simply omitted.
        var rosterIds = roster.Select(w => w.WaiterId).ToHashSet();
        var orphans = auxTables
            .Where(t => (t.AssignedWaiterId == null || !rosterIds.Contains(t.AssignedWaiterId.Value))
                        && ordersByTable.ContainsKey(t.Id))
            .ToList();

        if (orphans.Count > 0)
        {
            var orphanUserIds = orphans
                .Where(t => t.AssignedWaiterId != null)
                .Select(t => t.AssignedWaiterId!.Value)
                .Distinct()
                .ToList();
            var orphanUsers = await _db.Users
                .Where(u => orphanUserIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => new { u.FullName, u.AvatarPath });

            foreach (var aux in orphans)
            {
                var user = aux.AssignedWaiterId is Guid ownerId
                    ? orphanUsers.GetValueOrDefault(ownerId)
                    : null;

                rows.Add(new
                {
                    waiterId = aux.AssignedWaiterId,
                    waiterName = user?.FullName ?? "Ospătar șters",
                    avatarUrl = user == null || user.AvatarPath == null ? null : "/" + user.AvatarPath,
                    isActiveStaff = false,
                    auxTableId = (Guid?)aux.Id,
                    auxTableNumber = aux.TableNumber,
                    orders = ordersByTable[aux.Id].Select(MapOrder).ToList(),
                });
            }
        }

        return Ok(rows);
    }

    // ── Manager: send a parked order back to a real table ────────────────────

    [HttpPost("aux-tables/orders/{orderId:guid}/move-to-table")]
    public async Task<IActionResult> MoveAuxOrderToTable(
        Guid restaurantId,
        Guid orderId,
        [FromBody] MoveAuxOrderToTableRequest request)
    {
        if (!await _access.CanManageRestaurantAsync(UserId, restaurantId))
            return Forbid();

        var order = await _db.Orders
            .Include(o => o.Table)
            .FirstOrDefaultAsync(o => o.Id == orderId && o.DeletedAt == null);

        if (order == null)
            return NotFound(new { message = "Comanda nu a fost găsită sau a fost deja finalizată." });

        if (!ActiveStatuses.Contains(order.Status))
            return Conflict(new { message = $"Comanda nu mai e activă (stare curentă: {order.Status})." });

        if (!order.Table.IsAux)
            return Conflict(new { message = "Comanda nu se află pe o masă auxiliară." });

        // The tenant filter blocks cross-tenant ids automatically.
        var target = await _db.Tables
            .FirstOrDefaultAsync(t => t.Id == request.TableId && t.DeletedAt == null && !t.IsAux);
        if (target == null)
            return BadRequest(new { message = "Masa selectată nu există." });

        order.TableId = target.Id;
        order.MovedFromTableId = null;
        order.MovedToAuxAt = null;
        // Parking paused the auto-archive; landing back on a real table restarts
        // the countdown for orders already in the "ready" column.
        if (order.Status == "ready")
            order.ReadyAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        await BroadcastOrderMoved(order.RestaurantId, new
        {
            orderId = order.Id,
            orderNumber = order.OrderNumber,
            tableId = target.Id,
            tableNumber = target.TableNumber,
            isAux = false,
            auxWaiterId = (Guid?)null,
            auxWaiterName = (string?)null,
            movedFromTableId = (Guid?)null,
            movedFromTableNumber = (string?)null,
            sourceOrderId = order.Id,
        });

        _logger.LogInformation(
            "Order {OrderId} (#{Number}) returned from aux to table {TableId}",
            order.Id, order.OrderNumber, target.Id);

        return Ok(new
        {
            orderId = order.Id,
            orderNumber = order.OrderNumber,
            tableId = target.Id,
            tableNumber = target.TableNumber,
        });
    }

    // ── Manager: hand a parked order to another waiter's aux table ───────────

    [HttpPost("aux-tables/orders/{orderId:guid}/reassign")]
    public async Task<IActionResult> ReassignAuxOrder(
        Guid restaurantId,
        Guid orderId,
        [FromBody] ReassignAuxOrderRequest request)
    {
        if (!await _access.CanManageRestaurantAsync(UserId, restaurantId))
            return Forbid();

        var order = await _db.Orders
            .Include(o => o.Table)
            .FirstOrDefaultAsync(o => o.Id == orderId && o.DeletedAt == null);

        if (order == null)
            return NotFound(new { message = "Comanda nu a fost găsită sau a fost deja finalizată." });

        if (!ActiveStatuses.Contains(order.Status))
            return Conflict(new { message = $"Comanda nu mai e activă (stare curentă: {order.Status})." });

        if (!order.Table.IsAux)
            return Conflict(new { message = "Comanda nu se află pe o masă auxiliară." });

        if (!await _access.IsActiveWaiterAsync(request.WaiterId, restaurantId))
            return BadRequest(new { message = "Ospătarul selectat nu există sau nu este activ." });

        var waiterName = await _db.Users
            .Where(u => u.Id == request.WaiterId)
            .Select(u => u.FullName)
            .FirstOrDefaultAsync() ?? "Ospătar";

        var newAux = await GetOrCreateAuxTableAsync(request.WaiterId, waiterName);

        // Already parked there — nothing to change.
        if (order.TableId != newAux.Id)
        {
            // MovedFromTableId / MovedToAuxAt stay untouched: the incident's
            // origin and age follow the order between aux tables.
            order.TableId = newAux.Id;
            order.ClaimedByWaiterId = request.WaiterId;
            order.ClaimedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            string? movedFromTableNumber = null;
            if (order.MovedFromTableId is Guid fromId)
                movedFromTableNumber = await _db.Tables
                    .Where(t => t.Id == fromId)
                    .Select(t => t.TableNumber)
                    .FirstOrDefaultAsync();

            await BroadcastOrderMoved(order.RestaurantId, new
            {
                orderId = order.Id,
                orderNumber = order.OrderNumber,
                tableId = newAux.Id,
                tableNumber = newAux.TableNumber,
                isAux = true,
                auxWaiterId = (Guid?)request.WaiterId,
                auxWaiterName = (string?)waiterName,
                movedFromTableId = order.MovedFromTableId,
                movedFromTableNumber,
                sourceOrderId = order.Id,
            });

            _logger.LogInformation(
                "Order {OrderId} (#{Number}) reassigned to aux table {AuxTableId} of waiter {WaiterId}",
                order.Id, order.OrderNumber, newAux.Id, request.WaiterId);
        }

        return Ok(new
        {
            orderId = order.Id,
            orderNumber = order.OrderNumber,
            auxTableId = newAux.Id,
            auxTableNumber = newAux.TableNumber,
            auxWaiterId = request.WaiterId,
            auxWaiterName = waiterName,
        });
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    // Finds the waiter's live aux table or creates it. TableNumber is what the
    // kitchen kanban shows under "Masa", so it carries the waiter's name.
    private async Task<RestaurantTable> GetOrCreateAuxTableAsync(Guid waiterUserId, string waiterName)
    {
        var existing = await _db.Tables
            .FirstOrDefaultAsync(t => t.IsAux && t.AssignedWaiterId == waiterUserId && t.DeletedAt == null);
        if (existing != null) return existing;

        var label = $"AUX · {waiterName}";
        if (label.Length > 30) label = label[..30];

        var aux = new RestaurantTable
        {
            Id = Guid.NewGuid(),
            IsAux = true,
            AssignedWaiterId = waiterUserId,
            QrToken = null,
            ZoneId = null,
            Capacity = 1,
            Shape = "square",
            TableNumber = label,
        };
        _db.Tables.Add(aux);

        try
        {
            await _db.SaveChangesAsync();
            return aux;
        }
        catch (DbUpdateException)
        {
            // Two requests raced to create the same waiter's aux table and the
            // unique (restaurant_id, assigned_waiter_id) index rejected ours —
            // drop our attempt and use the winner.
            _db.Entry(aux).State = EntityState.Detached;
            return await _db.Tables
                .FirstAsync(t => t.IsAux && t.AssignedWaiterId == waiterUserId && t.DeletedAt == null);
        }
    }

    // Both staff surfaces (managers' console + waiters' board) track parked
    // orders, so every move is announced to both role groups.
    private async Task BroadcastOrderMoved(Guid restaurantId, object payload)
    {
        await _staffHub.Clients
            .Group(TenantGroups.Role(restaurantId, "admin"))
            .SendAsync("OrderMoved", payload);
        await _staffHub.Clients
            .Group(TenantGroups.Role(restaurantId, "WaitStaff"))
            .SendAsync("OrderMoved", payload);
    }
}

public class MoveOrderToAuxRequest
{
    // Whose aux table receives the order. Managers must pick a waiter; a waiter
    // may omit it (or send their own id) — only their own table is allowed.
    [JsonPropertyName("targetWaiterId")]
    public Guid? TargetWaiterId { get; set; }

    // Null = move the whole order. Otherwise the selected quantities split off
    // onto a new order parked on the aux table.
    [JsonPropertyName("items")]
    public List<MoveOrderItemSelection>? Items { get; set; }
}

public class MoveOrderItemSelection
{
    [JsonPropertyName("orderItemId")]
    public Guid OrderItemId { get; set; }

    [JsonPropertyName("quantity")]
    public int Quantity { get; set; }
}

public class MoveAuxOrderToTableRequest
{
    [JsonPropertyName("tableId")]
    public Guid TableId { get; set; }
}

public class ReassignAuxOrderRequest
{
    [JsonPropertyName("waiterId")]
    public Guid WaiterId { get; set; }
}
