using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.SignalR;
using RestaurantApi.Data;
using RestaurantApi.Models;
using RestaurantApi.Hubs;

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
    
    public FlashDealService(
        AppDbContext context,
        IHubContext<CustomerHub> customerHub,
        IHubContext<StaffHub> staffHub,
        IServiceScopeFactory scopeFactory
    )
    {
        _context = context;
        _customerhub = customerHub;
        _staffhub = staffHub;
        _scopeFactory=scopeFactory;
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
            OrderId = order.Id,
            ProductId = deal.ProductId.Value,
            Quantity = 1,
            UnitPriceAtPurchase = deal.DiscountPrice ?? 0m,
            IsFlashDealItem = true
        };

        order.Items.Add(orderItem); 
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

        var random = new Random();
        var selectedProduct = availableProducts[random.Next(availableProducts.Count)];

        var discountPrice= Math.Round(selectedProduct.Price * 0.8m, 2);

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

        await _customerhub.Clients.Group(order.TableId.ToString())
        .SendAsync("FlashDealAvailable", new
        {
            DealId= deal.Id,
            ProductName= selectedProduct.Name,
            DiscountPrice = deal.DiscountPrice,
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
}