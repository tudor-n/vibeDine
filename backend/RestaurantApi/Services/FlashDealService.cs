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
    Task EvaluateEligibilityAsync(Guid orderId);
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

        await _staffhub.Clients.Group("WaitStaff")
            .SendAsync("FlashDealClaimed", new { OrderId = order.Id, DealName = productName });

        return true;
    }

    public async Task EvaluateEligibilityAsync (Guid orderId)
    {
        var order = await _context.Orders
            .Include(o=> o.Items)
            .FirstOrDefaultAsync(o => o.Id == orderId);

        if (order == null) return;

        var orderedProductsIds = order.Items.Select(i => i.ProductId).ToList();

        var availableProducts = await _context.Products
            .Where(p=>p.IsAvailable ==true && !orderedProductsIds.Contains(p.Id))
            .ToListAsync();

        if (!availableProducts.Any()) return;

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
        await _customerhub.Clients.Group(order.TableId.ToString())
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


        var tableId = order.TableId.ToString();        
        var dealId = deal.Id;


        _ = Task.Run(async () =>
{
    await Task.Delay(TimeSpan.FromMinutes(2));

    using var scope = _scopeFactory.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var hub = scope.ServiceProvider.GetRequiredService<IHubContext<CustomerHub>>();

    var dealToExpire = await db.FlashDealOffers.FindAsync(dealId);
    if (dealToExpire == null || dealToExpire.Status != "pending") return;

    dealToExpire.Status = "expired";
    await db.SaveChangesAsync();

    await hub.Clients.Group(tableId)
        .SendAsync("FlashDealExpired", new { DealId = dealId });
});
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