using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.SignalR;
using System.Globalization;
using System.Text;
using System.Text.Json;
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
    private readonly ILogger<FlashDealService> _logger;
    
    public FlashDealService(
        AppDbContext context,
        IHubContext<CustomerHub> customerHub,
        IHubContext<StaffHub> staffHub,
        IServiceScopeFactory scopeFactory,
        ILogger<FlashDealService> logger
    )
    {
        _context = context;
        _customerhub = customerHub;
        _staffhub = staffHub;
        _scopeFactory = scopeFactory;
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

        if (deal.Status != "pending") return false;

        if (DateTime.UtcNow > deal.ExpiresAt) return false;

        if (deal.ProductId == null) return false;

        order.Items ??= new List<OrderItem>();

        bool alreadyClaimed = order.Items.Any(i => i.IsFlashDealItem == true && i.ProductId == deal.ProductId);
        if (alreadyClaimed) return false;

        var orderItem = new OrderItem
        {
            Id = Guid.NewGuid(),
            OrderId = order.Id,
            ProductId = deal.ProductId.Value,
            Quantity = 1,
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
        await _context.SaveChangesAsync();

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

        // --- Directive-aware product selection ---
        Product? selectedProduct = null;
        int discountPercent = 20; // default

        var activeDirective = await _context.AIDirectives
            .Where(d => d.IsActive == true)
            .OrderByDescending(d => d.CreatedAt)
            .FirstOrDefaultAsync();

        if (activeDirective != null)
        {
            // Extract discount % from the raw prompt up-front so it applies regardless of
            // which strategy locates the product
            if (!string.IsNullOrWhiteSpace(activeDirective.RawPrompt))
            {
                var promptForPercent = activeDirective.RawPrompt.ToLowerInvariant();
                var percentMatch = System.Text.RegularExpressions.Regex.Match(promptForPercent, @"(\d{1,3})\s*%");
                if (percentMatch.Success && int.TryParse(percentMatch.Groups[1].Value, out var pct) && pct > 0 && pct < 100)
                {
                    discountPercent = pct;
                }

                // Extract a min-order-total threshold like "peste 190 de lei" / "over 190 RON".
                // If the order doesn't meet it, no deal is offered for this submit.
                var thresholdMatch = System.Text.RegularExpressions.Regex.Match(
                    promptForPercent,
                    @"\b(peste|over|above|cel\s+pu[țt]in|at\s+least|min(?:imum)?)\s+(\d+(?:[.,]\d+)?)\s*(?:de\s+)?(?:lei|ron)?",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (thresholdMatch.Success && decimal.TryParse(
                        thresholdMatch.Groups[2].Value.Replace(',', '.'),
                        System.Globalization.NumberStyles.Number,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out var threshold))
                {
                    var orderTotal = order.Items.Sum(i => i.Quantity * i.UnitPriceAtPurchase);
                    if (orderTotal < threshold)
                    {
                        _logger.LogInformation(
                            "Flash deal skipped: order total {Total} below threshold {Threshold} (directive: {Prompt})",
                            orderTotal, threshold, activeDirective.RawPrompt);
                        return false;
                    }
                }
            }

            // Strategy 1: Try AI-parsed rules if available
            if (activeDirective.ParsedRules != null)
            {
                try
                {
                    var rules = JsonSerializer.Deserialize<JsonElement>(activeDirective.ParsedRules);
                    var itemHints = new List<string>();

                    if (rules.TryGetProperty("rules", out var rulesArray))
                    {
                        foreach (var rule in rulesArray.EnumerateArray())
                        {
                            if (!rule.TryGetProperty("action", out var actionProp)) continue;
                            var action = actionProp.GetString();

                            if (action == "recommend" && rule.TryGetProperty("item_hints", out var hints))
                            {
                                foreach (var hint in hints.EnumerateArray())
                                {
                                    var h = hint.GetString()?.ToLowerInvariant().Trim();
                                    if (!string.IsNullOrEmpty(h)) itemHints.Add(h);
                                }
                            }
                            else if (action == "pair" && rule.TryGetProperty("item_hint", out var singleHint))
                            {
                                var h = singleHint.GetString()?.ToLowerInvariant().Trim();
                                if (!string.IsNullOrEmpty(h)) itemHints.Add(h);
                            }
                        }
                    }

                    if (itemHints.Any())
                    {
                        // Diacritic-insensitive match so a hint like "limonada cu menta"
                        // still finds a product named "Limonadă cu mentă"
                        var normalizedHints = itemHints
                            .Select(RemoveDiacritics)
                            .ToList();

                        selectedProduct = availableProducts.FirstOrDefault(p =>
                        {
                            var normalizedName = RemoveDiacritics(p.Name.ToLowerInvariant());
                            return normalizedHints.Any(hint => normalizedName.Contains(hint));
                        });
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to parse directive rules for flash deal selection.");
                }
            }

            // Strategy 2: Parse the raw prompt directly for product name
            // Handles directives like "offer lemonade at 30% off" even when AI service is down
            if (selectedProduct == null && !string.IsNullOrWhiteSpace(activeDirective.RawPrompt))
            {
                var prompt = RemoveDiacritics(activeDirective.RawPrompt.ToLowerInvariant());

                // Match product by checking if any available product's full name appears in the prompt.
                // Strict full-name match: admins must spell out the exact item ("Limonadă cu mentă"),
                // since several variants of an item may exist on the menu.
                selectedProduct = availableProducts
                    .OrderByDescending(p => p.Name.Length)  // prefer longest match to avoid false positives
                    .FirstOrDefault(p => prompt.Contains(RemoveDiacritics(p.Name.ToLowerInvariant())));

                if (selectedProduct != null)
                    _logger.LogInformation("Flash deal: directive raw prompt matched product '{Name}' at {Pct}% off", selectedProduct.Name, discountPercent);
            }
        }

        // Fallback: random product, default discount
        if (selectedProduct == null)
        {
            selectedProduct = availableProducts[Random.Shared.Next(availableProducts.Count)];
            _logger.LogInformation("Flash deal: no directive match, selected random product '{Name}' at {Pct}% off", selectedProduct.Name, discountPercent);
        }

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

        // Send ProductId and OrderId so the frontend can claim correctly
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

    private static string RemoveDiacritics(string text)
    {
        var normalized = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        foreach (var c in normalized)
        {
            if (char.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }
}