using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using RestaurantApi.Data;
using RestaurantApi.Hubs;
using RestaurantApi.Models;
using RestaurantApi.Services;
using RestaurantApi.Tenancy;


namespace RestaurantApi.Controllers;

[ApiController]
[Authorize]
[Route("api/restaurants/{restaurantId:guid}/admin")]
public class AdminController : ControllerBase
{
    private readonly AppDbContext _dbContext;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IFlashDealService _flashDealService;
    private readonly IStockService _stockService;
    private readonly IHubContext<CustomerHub> _customerHub;
    private readonly IHubContext<StaffHub> _staffHub;
    private readonly ITenantAccess _access;
    private readonly ILogger <AdminController> _logger;

    private Guid UserId =>
        Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub")!);

    // Active-board statuses (the three kanban columns). Anything outside this
    // set is considered terminal and lives in the finalized log.
    private static readonly HashSet<string> ActiveStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "submitted", "preparing", "ready"
    };

    public AdminController(
        AppDbContext dbContext,
        IHttpClientFactory httpClientFactory,
        IFlashDealService flashDealService,
        IStockService stockService,
        IHubContext<CustomerHub> customerHub,
        IHubContext<StaffHub> staffHub,
        ITenantAccess access,
        ILogger<AdminController> logger)
    {
        _dbContext = dbContext;
        _httpClientFactory = httpClientFactory;
        _flashDealService = flashDealService;
        _stockService = stockService;
        _customerHub = customerHub;
        _staffHub = staffHub;
        _access = access;
        _logger = logger;
    }

    // ── Happy hour ───────────────────────────────────────────────────────────
    // One config per restaurant: a single product discounted by a fixed % during a
    // daily time window (restaurant-local). Deterministic — no AI. Replaces the old
    // free-text AI "directive". The per-order offer/claim pipeline downstream
    // (FlashDealOffer + SignalR) is unchanged; only how eligibility is decided changes.

    [HttpGet("happy-hour")]
    public async Task<IActionResult> GetHappyHour()
    {
        var config = await _dbContext.HappyHourDeals
            .Include(h => h.Product)
            .FirstOrDefaultAsync();

        return Ok(config is null ? null : ToHappyHourResponse(config));
    }

    [HttpPut("happy-hour")]
    public async Task<IActionResult> SaveHappyHour([FromBody] SaveHappyHourRequest request)
    {
        if (request.DiscountPercent < 1 || request.DiscountPercent > 99)
            return BadRequest(new { message = "Reducerea trebuie să fie între 1 și 99%." });

        if (!TimeOnly.TryParse(request.StartTime, CultureInfo.InvariantCulture, out var start) ||
            !TimeOnly.TryParse(request.EndTime, CultureInfo.InvariantCulture, out var end))
            return BadRequest(new { message = "Ore invalide. Folosește formatul HH:mm." });

        if (start == end)
            return BadRequest(new { message = "Ora de început și cea de sfârșit nu pot fi identice." });

        var product = await _dbContext.Products.FirstOrDefaultAsync(p => p.Id == request.ProductId);
        if (product is null)
            return BadRequest(new { message = "Produsul selectat nu există." });

        // Upsert the single per-restaurant row. RestaurantId is stamped on insert
        // by the tenant SaveChanges hook.
        var config = await _dbContext.HappyHourDeals.FirstOrDefaultAsync();
        if (config is null)
        {
            config = new HappyHourDeal { Id = Guid.NewGuid() };
            _dbContext.HappyHourDeals.Add(config);
        }

        config.ProductId = request.ProductId;
        config.DiscountPercent = request.DiscountPercent;
        config.StartTime = start;
        config.EndTime = end;
        config.IsEnabled = request.IsEnabled;
        config.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync();

        // `product` is tracked, so EF relationship fixup has populated config.Product.
        return Ok(ToHappyHourResponse(config));
    }

    [HttpDelete("happy-hour")]
    public async Task<IActionResult> DeleteHappyHour()
    {
        var config = await _dbContext.HappyHourDeals.FirstOrDefaultAsync();
        if (config is null) return NoContent();

        _dbContext.HappyHourDeals.Remove(config);
        await _dbContext.SaveChangesAsync();
        return NoContent();
    }

    private static object ToHappyHourResponse(HappyHourDeal h) => new
    {
        id = h.Id,
        productId = h.ProductId,
        productName = h.Product?.Name,
        discountPercent = h.DiscountPercent,
        startTime = h.StartTime.ToString("HH:mm"),
        endTime = h.EndTime.ToString("HH:mm"),
        isEnabled = h.IsEnabled,
        updatedAt = h.UpdatedAt
    };


    // ── Flash deal ───────────────────────────────────────────────────────────
    // One deterministic flash-deal rule per restaurant: a product offered at a
    // discount as the post-order claimable popup, optionally gated by a minimum
    // order total. Drives FlashDealService.EvaluateEligibilityAsync. Not time-gated
    // (that's happy hour) — it fires after an order is submitted.

    [HttpGet("flash-deal")]
    public async Task<IActionResult> GetFlashDealRule()
    {
        var rule = await _dbContext.FlashDealRules
            .Include(f => f.Product)
            .FirstOrDefaultAsync();

        return Ok(rule is null ? null : ToFlashDealResponse(rule));
    }

    [HttpPut("flash-deal")]
    public async Task<IActionResult> SaveFlashDealRule([FromBody] SaveFlashDealRequest request)
    {
        if (request.DiscountPercent < 1 || request.DiscountPercent > 99)
            return BadRequest(new { message = "Reducerea trebuie să fie între 1 și 99%." });

        if (request.MinOrderTotal is < 0m)
            return BadRequest(new { message = "Suma minimă nu poate fi negativă." });

        var product = await _dbContext.Products.FirstOrDefaultAsync(p => p.Id == request.ProductId);
        if (product is null)
            return BadRequest(new { message = "Produsul selectat nu există." });

        // Upsert the single per-restaurant row (RestaurantId stamped on insert).
        var rule = await _dbContext.FlashDealRules.FirstOrDefaultAsync();
        if (rule is null)
        {
            rule = new FlashDealRule { Id = Guid.NewGuid() };
            _dbContext.FlashDealRules.Add(rule);
        }

        rule.ProductId = request.ProductId;
        rule.DiscountPercent = request.DiscountPercent;
        rule.MinOrderTotal = request.MinOrderTotal;
        rule.IsEnabled = request.IsEnabled;
        rule.UpdatedAt = DateTime.UtcNow;
        // Attach the product we just validated so the response carries productName
        // (the nav isn't otherwise loaded on an upsert, which would echo back null
        // and make the admin's "current deal" card show a placeholder until reload).
        rule.Product = product;

        await _dbContext.SaveChangesAsync();
        return Ok(ToFlashDealResponse(rule));
    }

    [HttpDelete("flash-deal")]
    public async Task<IActionResult> DeleteFlashDealRule()
    {
        var rule = await _dbContext.FlashDealRules.FirstOrDefaultAsync();
        if (rule is null) return NoContent();

        _dbContext.FlashDealRules.Remove(rule);
        await _dbContext.SaveChangesAsync();
        return NoContent();
    }

    private static object ToFlashDealResponse(FlashDealRule f) => new
    {
        id = f.Id,
        productId = f.ProductId,
        productName = f.Product?.Name,
        discountPercent = f.DiscountPercent,
        minOrderTotal = f.MinOrderTotal,
        isEnabled = f.IsEnabled,
        updatedAt = f.UpdatedAt
    };

    [HttpGet("recommendation-directives")]
    public async Task<IActionResult> GetRecommendationDirectives()
    {
        var directives = await _dbContext.RecommendationDirectives
            .OrderByDescending(d => d.CreatedAt)
            .ToListAsync();

        return Ok(directives);
    }

    [HttpPost("recommendation-directives")]
    public async Task<IActionResult> CreateRecommendationDirective(
        [FromBody] CreateRecommendationDirectiveRequest request)
    {
        // Cap the prompt before it reaches the paid model / DB (cost + bloat guard).
        const int MaxRawPromptLength = 2000;
        var rawPrompt = (request.RawPrompt ?? string.Empty).Trim();
        if (rawPrompt.Length == 0)
            return BadRequest(new { message = "Textul directivei este obligatoriu." });
        if (rawPrompt.Length > MaxRawPromptLength)
            return BadRequest(new { message = $"Textul directivei poate avea cel mult {MaxRawPromptLength} de caractere." });

        var menuItems = await _dbContext.Products
            .Include(p => p.Category)
            .Where(p => p.IsAvailable == true)
            .Select(p => new
            {
                product_id = p.Id.ToString(),
                name = p.Name,
                category = p.Category != null ? p.Category.Name : null
            })
            .ToListAsync();

        var client = _httpClientFactory.CreateClient("AiService");
        string? parsedRules = null;

        try
        {
            var aiResponse = await client.PostAsJsonAsync(
                "/recommendation-directives/parse",
                new { raw_prompt = rawPrompt, menu_items = menuItems });

            if (aiResponse.IsSuccessStatusCode)
            {
                parsedRules = await aiResponse.Content.ReadAsStringAsync();
            }
            else
            {
                var body = await aiResponse.Content.ReadAsStringAsync();
                _logger.LogWarning(
                    "AI /recommendation-directives/parse returned non-success status {StatusCode}. Body: {Body}",
                    (int)aiResponse.StatusCode, body);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AI service failed to parse recommendation directive");
        }

        // Never persist a silently-inert directive: GetRuleDrivenRecommendationsAsync
        // filters on ParsedRules != null, so a null-rules row would look saved to the
        // admin yet never fire. Surface the AI outage instead of a false 200.
        if (parsedRules is null)
            return StatusCode(503, new
            {
                message = "Serviciul AI nu este disponibil momentan. Directiva nu a fost salvată. Încearcă din nou."
            });

        var directive = new RecommendationDirective
        {
            Id = Guid.NewGuid(),
            RawPrompt = rawPrompt,
            ParsedRules = parsedRules,
            CreatedAt = DateTime.UtcNow,
            IsActive = true
        };

        _dbContext.RecommendationDirectives.Add(directive);
        await _dbContext.SaveChangesAsync();

        return Ok(directive);
    }

    [HttpPatch("recommendation-directives/{id}")]
    public async Task<IActionResult> ToggleRecommendationDirective(Guid id, [FromBody] bool isActive)
    {
        var directive = await _dbContext.RecommendationDirectives.FindAsync(id);
        if (directive == null) return NotFound();

        directive.IsActive = isActive;
        await _dbContext.SaveChangesAsync();
        return Ok(directive);
    }

    [HttpDelete("recommendation-directives/{id}")]
    public async Task<IActionResult> DeleteRecommendationDirective(Guid id)
    {
        var directive = await _dbContext.RecommendationDirectives.FindAsync(id);
        if (directive == null) return NotFound();

        _dbContext.RecommendationDirectives.Remove(directive);
        await _dbContext.SaveChangesAsync();
        return NoContent();
    }


    [HttpGet("similarity-rules")]
    public async Task<IActionResult> GetSimilarityRules()
    {
        var rules = await _dbContext.SimilarityRules
            .Include(r => r.ProductA)
            .Include(r => r.ProductB)
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new
            {
                r.Id,
                r.ProductAId,
                productAName = r.ProductA != null ? r.ProductA.Name : null,
                r.ProductBId,
                productBName = r.ProductB != null ? r.ProductB.Name : null,
                r.Score,
                r.RuleType,
                r.CreatedAt
            })
            .ToListAsync();

        return Ok(rules);
    }

    [HttpPost("similarity-rules/auto-generate")]
    public async Task<IActionResult> AutoGenerateRules()
    {
        var client = _httpClientFactory.CreateClient("AiService");

        // Bound the history fed to the model: load only the most recent orders rather
        // than the tenant's entire order table (memory spike + cost amplification).
        const int MaxOrderHistory = 500;
        var orders = await _dbContext.Orders
            .Include(o => o.Items)
            .OrderByDescending(o => o.CreatedAt)
            .Take(MaxOrderHistory)
            .ToListAsync();
        var history = orders.Select(o => o.Items.Select(i => i.ProductId.ToString()).ToList()).ToList();

        try 
        {
            var aiResponse = await client.PostAsJsonAsync("/similarity/auto-generate", new { order_history = history });
            if (aiResponse.IsSuccessStatusCode)
            {
                var result = await aiResponse.Content.ReadFromJsonAsync<JsonElement>();
                return Ok(result);
            }
        }
        catch 
        {
            return StatusCode(503, new { message = "AI Service unavailable" });
        }

        return StatusCode(500);
    }


    [HttpGet("metrics")]
    public async Task <IActionResult> GetMetrics()
    {
        var totalDeals = await _dbContext.FlashDealOffers.CountAsync();
        var claimedDeals = await _dbContext.FlashDealOffers.CountAsync(d=>d.Status=="claimed");
        var expiredDeals = await _dbContext.FlashDealOffers.CountAsync(d=>d.Status=="expired");

        var upsellRevenue = await _dbContext.OrderItems
        .Where(i=>i.WasAiRecommended==true)
        .SumAsync(i => (decimal?)(i.Quantity * i.UnitPriceAtPurchase)) ?? 0;

        var flashDealRevenue = await _dbContext.OrderItems
        .Where(i=>i.IsFlashDealItem==true)
        .SumAsync(i => (decimal?)(i.Quantity * i.UnitPriceAtPurchase)) ?? 0;

        return Ok(new
        {
           flashDealsShown = totalDeals,        
           flashDealsClaimed = claimedDeals,    
           flashDealsExpired = expiredDeals,    
           upsellRevenue = upsellRevenue,
           flashDealRevenue = flashDealRevenue
        });
    }

    // Finalized log. Returns soft-deleted orders (the natural outcome of the
    // 45-second grace period) and explicitly cancelled ones. Active orders
    // surface via /admin/orders/active instead. Tenant scoping is handled by
    // the global query filter on Order.RestaurantId.
    [HttpGet("orders")]
    public async Task<IActionResult> GetOrders([FromQuery] int limit = 100)
    {
        var log = await _dbContext.Orders
            .Where(o => o.DeletedAt != null || o.Status == "cancelled" || o.Status == "completed")
            .Include(o => o.Table)
            .Include(o => o.Items)
                .ThenInclude(i => i.Product)
            .OrderByDescending(o => o.CreatedAt)
            .Take(limit)
            .Select(o => new
            {
                id = o.Id,
                orderNumber = o.OrderNumber,
                status = o.Status,
                paymentStatus = o.PaymentStatus,
                totalAmount = o.TotalAmount,
                createdAt = o.CreatedAt,
                deletedAt = o.DeletedAt,
                tableNumber = o.Table != null ? o.Table.TableNumber : "?",
                items = o.Items.Select(i => new
                {
                    id = i.Id,
                    productName = i.Product != null ? i.Product.Name : "Produs șters",
                    quantity = i.Quantity,
                    unitPrice = i.UnitPriceAtPurchase,
                    subtotal = i.Quantity * i.UnitPriceAtPurchase,
                    wasAiRecommended = i.WasAiRecommended ?? false,
                    isFlashDealItem = i.IsFlashDealItem ?? false
                }).ToList()
            })
            .ToListAsync();

        return Ok(log);
    }

    // Active kanban feed. The admin frontend polls this every few seconds and
    // bucket-sorts the result into the three columns (submitted / preparing / ready).
    [HttpGet("orders/active")]
    public async Task<IActionResult> GetActiveOrders()
    {
        var orders = await _dbContext.Orders
            .Where(o => o.DeletedAt == null
                        && (o.Status == "submitted" || o.Status == "preparing" || o.Status == "ready"))
            .Include(o => o.Table)
            .Include(o => o.ClaimedByWaiter)
            .Include(o => o.Items)
                .ThenInclude(i => i.Product)
            .OrderBy(o => o.CreatedAt)
            .Select(o => new
            {
                id = o.Id,
                orderNumber = o.OrderNumber,
                status = o.Status,
                paymentStatus = o.PaymentStatus,
                totalAmount = o.TotalAmount,
                createdAt = o.CreatedAt,
                readyAt = o.ReadyAt,
                tableId = o.TableId,
                tableNumber = o.Table != null ? o.Table.TableNumber : "?",
                isAuxTable = o.Table != null && o.Table.IsAux,
                auxWaiterName = o.Table != null && o.Table.AssignedWaiter != null ? o.Table.AssignedWaiter.FullName : null,
                movedFromTableId = o.MovedFromTableId,
                claimedByWaiterId = o.ClaimedByWaiterId,
                claimedByName = o.ClaimedByWaiter != null ? o.ClaimedByWaiter.FullName : null,
                claimedAt = o.ClaimedAt,
                items = o.Items.Select(i => new
                {
                    id = i.Id,
                    productName = i.Product != null ? i.Product.Name : "Produs șters",
                    quantity = i.Quantity,
                    unitPrice = i.UnitPriceAtPurchase,
                    subtotal = i.Quantity * i.UnitPriceAtPurchase,
                    wasAiRecommended = i.WasAiRecommended ?? false,
                    isFlashDealItem = i.IsFlashDealItem ?? false
                }).ToList()
            })
            .ToListAsync();

        return Ok(orders);
    }

    public class UpdateOrderStatusRequest
    {
        public string Status { get; set; } = string.Empty;
    }

    // Kanban drag-drop target. Only transitions between submitted/preparing/ready
    // are allowed here — terminal transitions (completed) are owned by the
    // OrderFinalizationService once the grace period elapses.
    [HttpPatch("orders/{orderId:guid}/status")]
    public async Task<IActionResult> UpdateOrderStatus(
        Guid restaurantId,
        Guid orderId,
        [FromBody] UpdateOrderStatusRequest request)
    {
        var newStatus = request?.Status?.Trim().ToLowerInvariant() ?? string.Empty;
        if (!ActiveStatuses.Contains(newStatus))
            return BadRequest(new { message = $"Status '{request?.Status}' nepermis. Folosește submitted/preparing/ready." });

        var order = await _dbContext.Orders
            .FirstOrDefaultAsync(o => o.Id == orderId && o.DeletedAt == null);

        if (order == null)
            return NotFound(new { message = "Comanda nu a fost găsită sau a fost deja finalizată." });

        if (!ActiveStatuses.Contains(order.Status))
            return Conflict(new { message = $"Comanda nu mai e activă (stare curentă: {order.Status})." });

        // Managers (OrgOwner of the org, RestaurantAdmin of this restaurant) may move
        // any order on the board. A waiter may only move an order they personally
        // claimed — never an unclaimed one, nor one claimed by a colleague or a
        // manager. Mirrors the floor-plan claim model and the board's drag rules.
        var canManage = await _access.CanManageRestaurantAsync(UserId, restaurantId);
        if (!canManage && order.ClaimedByWaiterId != UserId)
            return StatusCode(StatusCodes.Status403Forbidden,
                new { message = "Poți modifica doar comenzile preluate de tine." });

        order.Status = newStatus;
        // Stamp ReadyAt when entering "ready" (starts the soft-delete countdown);
        // clear it when the admin drags back out, so the timer resets cleanly.
        order.ReadyAt = newStatus == "ready" ? DateTime.UtcNow : null;

        await _dbContext.SaveChangesAsync();

        // Broadcast to the customer at that table so the OrderSentView timeline
        // updates live.
        await _customerHub.Clients
            .Group(TenantGroups.Table(order.RestaurantId, order.TableId))
            .SendAsync("OrderStatusUpdated", new
            {
                orderId = order.Id,
                status = order.Status,
            });

        // Parked orders: the customer joined the ORIGINAL table's group, so mirror the
        // status broadcast there or their live timeline goes silent while it's parked.
        if (order.MovedFromTableId is Guid homeTableId)
            await _customerHub.Clients
                .Group(TenantGroups.Table(order.RestaurantId, homeTableId))
                .SendAsync("OrderStatusUpdated", new { orderId = order.Id, status = order.Status });

        // And to other admin sessions so the kanban stays in sync across tabs.
        await _staffHub.Clients
            .Group(TenantGroups.Role(order.RestaurantId, "admin"))
            .SendAsync("OrderStatusUpdated", new
            {
                orderId = order.Id,
                orderNumber = order.OrderNumber,
                status = order.Status,
                readyAt = order.ReadyAt,
                tableId = order.TableId,
            });

        return Ok(new
        {
            id = order.Id,
            orderNumber = order.OrderNumber,
            status = order.Status,
            readyAt = order.ReadyAt,
        });
    }

    // Cancels an active order: marks it cancelled, drops it off the board (soft-delete so it
    // lands in the finalized log), and restores the stock that was decremented at dispatch.
    // No cancel handler existed before this feature — UpdateOrderStatus rejects anything outside
    // submitted/preparing/ready, so cancellation needs its own endpoint.
    [HttpPost("orders/{orderId:guid}/cancel")]
    public async Task<IActionResult> CancelOrder(Guid restaurantId, Guid orderId)
    {
        var order = await _dbContext.Orders
            .FirstOrDefaultAsync(o => o.Id == orderId && o.DeletedAt == null);

        if (order == null)
            return NotFound(new { message = "Comanda nu a fost găsită sau a fost deja finalizată." });

        // Only orders still on the active board can be cancelled (these are the ones whose stock
        // was decremented at dispatch). Completed/already-cancelled/merged orders are terminal.
        if (!ActiveStatuses.Contains(order.Status))
            return Conflict(new { message = $"Comanda nu mai poate fi anulată (stare: {order.Status})." });

        order.Status = "cancelled";
        order.DeletedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();

        // Put the ingredients back (idempotent via Order.StockDecremented) — this also broadcasts
        // the resulting availability changes so any out-of-stock items un-blur on the menu.
        await _stockService.ApplyRestoreForOrderAsync(order);

        // Customer at the table sees the cancelled state on their status screen.
        await _customerHub.Clients
            .Group(TenantGroups.Table(order.RestaurantId, order.TableId))
            .SendAsync("OrderStatusUpdated", new { orderId = order.Id, status = order.Status });

        // Parked orders: the customer joined the ORIGINAL table's group, so mirror the
        // status broadcast there or their live timeline goes silent while it's parked.
        if (order.MovedFromTableId is Guid homeTableId)
            await _customerHub.Clients
                .Group(TenantGroups.Table(order.RestaurantId, homeTableId))
                .SendAsync("OrderStatusUpdated", new { orderId = order.Id, status = order.Status });

        // Other admin tabs drop the card from the board.
        await _staffHub.Clients
            .Group(TenantGroups.Role(order.RestaurantId, "admin"))
            .SendAsync("OrderStatusUpdated", new { orderId = order.Id, orderNumber = order.OrderNumber, status = order.Status });

        _logger.LogInformation("Cancelled order {OrderId} (#{Number})", order.Id, order.OrderNumber);
        return Ok(new { id = order.Id, status = order.Status });
    }

    // Waiter (or manager) takes ownership of an order from the floor plan
    // ("Preia comanda"). Until claimed, the order shows on the Comenzi board
    // greyed-out and immutable; claiming locks it to one person so two staffers
    // never prepare the same order.
    [HttpPost("orders/{orderId:guid}/claim")]
    public async Task<IActionResult> ClaimOrder(Guid restaurantId, Guid orderId)
    {
        if (!await _access.CanAccessRestaurantAsync(UserId, restaurantId))
            return Forbid();

        var order = await _dbContext.Orders
            .Include(o => o.ClaimedByWaiter)
            .FirstOrDefaultAsync(o => o.Id == orderId && o.DeletedAt == null);

        if (order == null)
            return NotFound(new { message = "Comanda nu a fost găsită sau a fost deja finalizată." });

        if (!ActiveStatuses.Contains(order.Status))
            return Conflict(new { message = $"Comanda nu mai e activă (stare curentă: {order.Status})." });

        // Already claimed by someone else → only a manager may take it over.
        if (order.ClaimedByWaiterId is Guid currentOwner && currentOwner != UserId)
        {
            if (!await _access.CanManageRestaurantAsync(UserId, restaurantId))
                return Conflict(new
                {
                    message = $"Comanda a fost deja preluată de {order.ClaimedByWaiter?.FullName ?? "alt coleg"}.",
                });
        }

        order.ClaimedByWaiterId = UserId;
        order.ClaimedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();

        var claimerName = await _dbContext.Users
            .Where(u => u.Id == UserId)
            .Select(u => u.FullName)
            .FirstOrDefaultAsync();

        await BroadcastClaim(order, claimerName);

        return Ok(new
        {
            id = order.Id,
            claimedByWaiterId = order.ClaimedByWaiterId,
            claimedByName = claimerName,
            claimedAt = order.ClaimedAt,
        });
    }

    // Hands a claimed order back to the pool. The claimer can release their own;
    // managers can release anyone's.
    [HttpPost("orders/{orderId:guid}/release")]
    public async Task<IActionResult> ReleaseOrder(Guid restaurantId, Guid orderId)
    {
        if (!await _access.CanAccessRestaurantAsync(UserId, restaurantId))
            return Forbid();

        var order = await _dbContext.Orders
            .FirstOrDefaultAsync(o => o.Id == orderId && o.DeletedAt == null);

        if (order == null)
            return NotFound(new { message = "Comanda nu a fost găsită." });

        if (order.ClaimedByWaiterId == null)
            return Ok(new { id = order.Id, claimedByWaiterId = (Guid?)null });

        if (order.ClaimedByWaiterId != UserId
            && !await _access.CanManageRestaurantAsync(UserId, restaurantId))
            return Forbid();

        order.ClaimedByWaiterId = null;
        order.ClaimedAt = null;
        await _dbContext.SaveChangesAsync();

        await BroadcastClaim(order, null);

        return Ok(new { id = order.Id, claimedByWaiterId = (Guid?)null });
    }

    // Notifies every staff session (admin + waiters) so the Comenzi board, the
    // floor plan, and the modal all reflect the new claim state in real time.
    private async Task BroadcastClaim(Order order, string? claimedByName)
    {
        var payload = new
        {
            orderId = order.Id,
            orderNumber = order.OrderNumber,
            tableId = order.TableId,
            claimedByWaiterId = order.ClaimedByWaiterId,
            claimedByName,
            claimedAt = order.ClaimedAt,
        };
        await _staffHub.Clients
            .Group(TenantGroups.Role(order.RestaurantId, "admin"))
            .SendAsync("OrderClaimed", payload);
        await _staffHub.Clients
            .Group(TenantGroups.Role(order.RestaurantId, "WaitStaff"))
            .SendAsync("OrderClaimed", payload);
    }

    [HttpGet("flash-deals")]
    public async Task <IActionResult> GetFlashDeals()
    {
        var deals= await _dbContext.FlashDealOffers
        .Include(d=>d.Product)
        .OrderByDescending(d => d.ExpiresAt)
        .Select(d => new
        {
            d.Id,
            d.Status,
            d.DiscountPrice,
            d.ExpiresAt,
            d.OrderId,
            ProductName = d.Product != null ? d.Product.Name : "Unknown"
        }).ToListAsync();

     return Ok(deals);
    
    }
   
   [HttpPost("flash-deals/trigger")]
   public async Task<IActionResult> TriggerFlashDeal([FromBody] Guid orderId)
    {
        await _flashDealService.EvaluateEligibilityAsync(orderId);
        return Ok(new {message = "Flash deal triggered successfully."});
    }

}

public class SaveHappyHourRequest
{
    [JsonPropertyName("productId")]
    public Guid ProductId { get; set; }

    [JsonPropertyName("discountPercent")]
    public int DiscountPercent { get; set; }

    // "HH:mm" wall-clock strings (from <input type="time">), parsed server-side to
    // TimeOnly — avoids System.Text.Json TimeOnly format ambiguity over the wire.
    [JsonPropertyName("startTime")]
    public string StartTime { get; set; } = string.Empty;

    [JsonPropertyName("endTime")]
    public string EndTime { get; set; } = string.Empty;

    [JsonPropertyName("isEnabled")]
    public bool IsEnabled { get; set; } = true;
}

public class SaveFlashDealRequest
{
    [JsonPropertyName("productId")]
    public Guid ProductId { get; set; }

    [JsonPropertyName("discountPercent")]
    public int DiscountPercent { get; set; }

    // Optional minimum order total to gate the offer. Null = offer after every order.
    [JsonPropertyName("minOrderTotal")]
    public decimal? MinOrderTotal { get; set; }

    [JsonPropertyName("isEnabled")]
    public bool IsEnabled { get; set; } = true;
}

public class CreateRecommendationDirectiveRequest
{
    [JsonPropertyName("rawPrompt")]
    public string RawPrompt { get; set; } = string.Empty;
}