using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.SignalR;
using RestaurantApi.Data;
using RestaurantApi.Models;
using RestaurantApi.Hubs;
using Microsoft.Extensions.Logging;

namespace RestaurantApi.Services;

public interface IFlashDealService
{
    Task<bool> ClaimDealAsync(Guid orderId, Guid dealId);
    Task<bool> EvaluateEligibilityAsync(Guid orderId);
    Task DispatchToKitchenAsync(Guid orderId);
}

public class FlashDealService : IFlashDealService
{
    private readonly AppDbContext _context;
    private readonly IHubContext<CustomerHub> _customerhub;
    private readonly IHubContext<StaffHub> _staffhub;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IStockService _stockService;
    private readonly IOrderEventWriter _events;
    private readonly ILogger<FlashDealService> _logger;

    public FlashDealService(
        AppDbContext context,
        IHubContext<CustomerHub> customerHub,
        IHubContext<StaffHub> staffHub,
        IServiceScopeFactory scopeFactory,
        IStockService stockService,
        IOrderEventWriter events,
        ILogger<FlashDealService> logger
    )
    {
        _context = context;
        _customerhub = customerHub;
        _staffhub = staffHub;
        _scopeFactory = scopeFactory;
        _stockService = stockService;
        _events = events;
        _logger = logger;
    }

    public async Task<bool> ClaimDealAsync(Guid orderId, Guid dealId)
    {
        var deal = await _context.FlashDealOffers
            .Include(d => d.Product)
            .FirstOrDefaultAsync(d => d.Id == dealId);

        var order = await _context.Orders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == orderId);

        if (deal == null || order == null) return false;

        // The deal must belong to this order. Both are loaded independently under the
        // tenant filter, so without this check a guest could claim a still-pending
        // deal generated for a *different* order in the same restaurant onto their bill.
        if (deal.OrderId != order.Id) return false;

        if (deal.Status != "pending") return false;

        if (DateTime.UtcNow > deal.ExpiresAt) return false;

        if (deal.ProductId == null) return false;

        order.Items ??= new List<OrderItem>();

        bool alreadyClaimed = order.Items.Any(i => i.IsFlashDealItem == true && i.ProductId == deal.ProductId);
        if (alreadyClaimed) return false;

        // One discounted portion per guest whose cart is on this bill. A solo /
        // pay-separately order (nobody merged) claims quantity 1; a bill that
        // merged 3 guests claims 3 — each at the discounted unit price, so the
        // line total is unit × guests with the discount already baked into the
        // unit (e.g. 3 × 16 = 48 for a 20-lei item at 20% off).
        var dealQuantity = await CountCombinedGuestsAsync(order.Id);

        var orderItem = new OrderItem
        {
            Id = Guid.NewGuid(),
            OrderId = order.Id,
            ProductId = deal.ProductId.Value,
            Quantity = dealQuantity,
            UnitPriceAtPurchase = deal.DiscountPrice ?? 0m,
            IsFlashDealItem = true,
            WasAiRecommended = false
        };

        _context.OrderItems.Add(orderItem);
        // EF Core fixup already added orderItem to order.Items, so Sum covers everything
        order.TotalAmount = order.Items.Sum(i => i.Quantity * i.UnitPriceAtPurchase);
        deal.Status = "claimed";
        await _context.SaveChangesAsync();

        var productName = deal.Product?.Name ?? "Special Item";

        await _staffhub.Clients.Group(TenantGroups.Role(order.RestaurantId, "WaitStaff"))
            .SendAsync("FlashDealClaimed", new { OrderId = order.Id, DealName = productName });

        // Claiming the deal dispatches the order to the kitchen immediately,
        // skipping the 2-minute hold that the SubmitOrder flow applies.
        await DispatchToKitchenAsync(orderId);

        return true;
    }

    // Transitions a `pending_dispatch` order to `submitted` and notifies the
    // kitchen kanban. Idempotent: if the order is already past pending_dispatch
    // (e.g. claim fired right before the 2-min expiry task), this no-ops.
    public async Task DispatchToKitchenAsync(Guid orderId)
    {
        var order = await _context.Orders
            .FirstOrDefaultAsync(o => o.Id == orderId);

        if (order == null || order.Status != "pending_dispatch") return;

        order.Status = "submitted";

        // Dispatch is also the point Logistic cares about (consumption valuation, and the
        // order's line detail for the eventual receipt). Attach the outbox row to the
        // context BEFORE the decrement call: that call is what saves — inside its own
        // transaction — so the event rides the same commit as the status flip and the
        // stock movement. The early return above keeps this exactly-once.
        _events.Enqueue(
            _context,
            OrderEventTypes.OrderDispatched,
            order.RestaurantId,
            order.Id,
            await OrderEventPayloads.BuildOrderPayloadAsync(_context, order));

        // Sending to the kitchen is the commitment point: subtract the order's ingredient usage
        // from stock here. Idempotent, and persists the status change in the same transaction
        // (handles recipe, simple and untracked products alike).
        await _stockService.ApplyDecrementForOrderAsync(order);

        await _staffhub.Clients
            .Group(TenantGroups.Role(order.RestaurantId, "admin"))
            .SendAsync("OrderSubmitted", new
            {
                orderId = order.Id,
                orderNumber = order.OrderNumber,
                tableId = order.TableId,
                status = order.Status,
            });
    }

    // Returns true if a deal offer was created (so the caller should hold the
    // order in pending_dispatch); false if no deal was issued (caller should
    // dispatch immediately because there's nothing for the customer to wait on).
    public async Task<bool> EvaluateEligibilityAsync (Guid orderId)
    {
        var order = await _context.Orders
            .Include(o=> o.Items)
            .FirstOrDefaultAsync(o => o.Id == orderId);

        if (order == null) return false;

        var orderedProductsIds = order.Items.Select(i => i.ProductId).ToList();

        var availableProducts = await _context.Products
            .Where(p=>p.IsAvailable ==true && !orderedProductsIds.Contains(p.Id))
            .ToListAsync();

        if (!availableProducts.Any()) return false;

        // --- Flash-deal selection (deterministic; no AI) ---
        // One enabled flash-deal rule per restaurant. Unlike happy hour this is NOT
        // time-gated — it fires after an order is submitted, optionally gated by a
        // minimum order total. The configured product, or nothing (no random fallback).
        var rule = await _context.FlashDealRules
            .FirstOrDefaultAsync(r => r.IsEnabled == true);

        if (rule == null) return false;

        if (rule.MinOrderTotal is decimal threshold)
        {
            var orderTotal = order.Items.Sum(i => i.Quantity * i.UnitPriceAtPurchase);
            if (orderTotal < threshold)
            {
                _logger.LogInformation(
                    "Flash deal skipped: order total {Total} below minimum {Threshold}",
                    orderTotal, threshold);
                return false;
            }
        }

        // availableProducts already excludes items already on the order and
        // unavailable products. Deterministic: the configured item or nothing.
        var selectedProduct = availableProducts.FirstOrDefault(p => p.Id == rule.ProductId);
        if (selectedProduct == null) return false;

        var discountPercent = rule.DiscountPercent;

        _logger.LogInformation(
            "Flash deal: offering '{Name}' at {Pct}% off", selectedProduct.Name, discountPercent);

        var discountPrice = Math.Round(selectedProduct.Price * (1 - discountPercent / 100m), 2);
        var expiresAt = DateTime.UtcNow.AddMinutes(2);

        var deal = new FlashDealOffer
        {
            OrderId=orderId,
            ProductId=selectedProduct.Id,
            DiscountPrice=discountPrice,
            ExpiresAt= expiresAt,
            Status ="pending"
        };

        _context.FlashDealOffers.Add(deal);
        await _context.SaveChangesAsync();

        // Scale the offer to the party size when this bill merged several guests:
        // one discounted portion each. Stays 1 for a solo / pay-separately order.
        var combinedGuests = await CountCombinedGuestsAsync(orderId);

        // Send ProductId and OrderId so the frontend can claim correctly. The
        // table SignalR group is the only group phones are in, so every phone
        // receives this — the customer app filters by OrderId so only the phone
        // that owns this order pops the offer (separate bills => separate deals).
        await _customerhub.Clients.Group(TenantGroups.Table(order.RestaurantId, order.TableId))
            .SendAsync("FlashDealAvailable", new
            {
                DealId= deal.Id,
                OrderId = orderId,
                ProductId = selectedProduct.Id,
                ProductName= selectedProduct.Name,
                OriginalPrice = selectedProduct.Price,
                DiscountPrice = deal.DiscountPrice,
                DiscountPercent = discountPercent,
                Quantity = combinedGuests,
                ExpiresAt=deal.ExpiresAt
            });


        var restaurantId = order.RestaurantId;
        var tableId = order.TableId;
        var dealId = deal.Id;
        var dealOrderId = orderId;

        _ = Task.Run(async () =>
{
    await Task.Delay(TimeSpan.FromMinutes(2));

    using var scope = _scopeFactory.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var hub = scope.ServiceProvider.GetRequiredService<IHubContext<CustomerHub>>();
    var flashService = scope.ServiceProvider.GetRequiredService<IFlashDealService>();

    // The background scope has no ambient request, so set the tenant explicitly
    // for the query filter to find the deal.
    scope.ServiceProvider.GetRequiredService<RestaurantApi.Tenancy.ICurrentTenant>()
        .SetRestaurant(restaurantId);

    var dealToExpire = await db.FlashDealOffers.FindAsync(dealId);
    if (dealToExpire != null && dealToExpire.Status == "pending")
    {
        dealToExpire.Status = "expired";
        await db.SaveChangesAsync();

        await hub.Clients.Group(TenantGroups.Table(restaurantId, tableId))
            .SendAsync("FlashDealExpired", new { DealId = dealId });
    }

    // The 2-minute hold is up — release the order to the kitchen if it wasn't
    // already dispatched via ClaimDealAsync.
    await flashService.DispatchToKitchenAsync(dealOrderId);
});

        return true;
    }

    // How many guests' carts ended up on this bill: the order's own owner plus
    // every draft that was merged into it. Returns 1 for a solo / pay-separately
    // order (nobody merged). The count is stable once the order leaves "draft" —
    // merges only re-parent drafts, so nothing new can attach to a submitted
    // order — which is why the offer-time and claim-time counts always agree.
    private async Task<int> CountCombinedGuestsAsync(Guid orderId)
    {
        var mergedDrafts = await _context.Orders
            .CountAsync(o => o.MergedIntoOrderId == orderId);
        return mergedDrafts + 1;
    }
}