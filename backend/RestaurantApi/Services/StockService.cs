using Microsoft.EntityFrameworkCore;
using RestaurantApi.Data;
using RestaurantApi.Models;

namespace RestaurantApi.Services;

public interface IStockService
{
    /// <summary>
    /// Subtracts the order's ingredient usage from stock and marks the order decremented.
    /// Persists the order (whose status/flag the caller has already set on the shared context)
    /// together with the stock changes in one transaction. Idempotent: a no-op if the order was
    /// already decremented.
    /// </summary>
    Task ApplyDecrementForOrderAsync(Order order);

    /// <summary>
    /// Reverses a prior decrement (used when an order is cancelled) and clears the flag.
    /// Idempotent: a no-op if the order was never decremented.
    /// </summary>
    Task ApplyRestoreForOrderAsync(Order order);
}

public class StockService : IStockService
{
    private readonly AppDbContext _db;
    private readonly ILogger<StockService> _logger;

    public StockService(AppDbContext db, ILogger<StockService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public Task ApplyDecrementForOrderAsync(Order order) => ApplyAsync(order, restore: false);

    public Task ApplyRestoreForOrderAsync(Order order) => ApplyAsync(order, restore: true);

    private async Task ApplyAsync(Order order, bool restore)
    {
        // Idempotency: decrement runs at most once; restore only reverses a real decrement.
        if (!restore && order.StockDecremented) return;
        if (restore && !order.StockDecremented) return;

        var deltas = await ComputeIngredientDeltasAsync(order.Id);

        order.StockDecremented = !restore;

        // No tracked items → just persist the order (status/flag the caller already set).
        if (deltas.Count == 0)
        {
            await _db.SaveChangesAsync();
            return;
        }

        // Atomic quantity math via ExecuteUpdate (SQL `quantity = quantity ± delta`) so two orders
        // racing for the last unit can't lose an update. We deliberately allow a transient negative
        // and surface the item as unavailable rather than hard-blocking the order. Wrapped in a
        // transaction with the order save so status + flag + stock move together (or not at all).
        await using var tx = await _db.Database.BeginTransactionAsync();
        await _db.SaveChangesAsync();

        foreach (var (ingredientId, qty) in deltas)
        {
            if (restore)
            {
                await _db.Ingredients
                    .Where(i => i.Id == ingredientId)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.QuantityOnHand, x => x.QuantityOnHand + qty));
            }
            else
            {
                await _db.Ingredients
                    .Where(i => i.Id == ingredientId)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.QuantityOnHand, x => x.QuantityOnHand - qty));
            }
        }

        await tx.CommitAsync();

        _logger.LogInformation(
            "Stock {Action} for order {OrderId}: {Count} ingredient(s) adjusted.",
            restore ? "restored" : "decremented", order.Id, deltas.Count);
    }

    /// <summary>
    /// Maps each ingredient the order consumes to the total quantity used, summed across all line
    /// items. Recipe products contribute their recipe lines × line quantity; simple products
    /// contribute their linked ingredient × line quantity; untracked products contribute nothing.
    /// All queries run under the ambient tenant filter, so only this restaurant's stock is touched.
    /// </summary>
    private async Task<Dictionary<Guid, decimal>> ComputeIngredientDeltasAsync(Guid orderId)
    {
        var items = await _db.OrderItems
            .Where(i => i.OrderId == orderId)
            .Select(i => new { i.ProductId, i.Quantity })
            .ToListAsync();

        var deltas = new Dictionary<Guid, decimal>();
        if (items.Count == 0) return deltas;

        var productIds = items.Select(i => i.ProductId).Distinct().ToList();

        var modes = await _db.Products
            .Where(p => productIds.Contains(p.Id))
            .Select(p => new { p.Id, p.StockTrackingMode })
            .ToDictionaryAsync(p => p.Id, p => p.StockTrackingMode);

        var recipeProductIds = modes.Where(m => m.Value == StockTrackingMode.Recipe).Select(m => m.Key).ToList();
        var simpleProductIds = modes.Where(m => m.Value == StockTrackingMode.Simple).Select(m => m.Key).ToList();

        // Recipe lines for every recipe-tracked product in the order, grouped by product.
        var recipeByProduct = (await _db.RecipeItems
                .Where(r => recipeProductIds.Contains(r.ProductId))
                .Select(r => new { r.ProductId, r.IngredientId, r.QuantityPerPortion })
                .ToListAsync())
            .GroupBy(r => r.ProductId)
            .ToDictionary(g => g.Key, g => g.Select(r => (r.IngredientId, r.QuantityPerPortion)).ToList());

        // Linked stock row for each simple-tracked product (productId -> ingredientId).
        var simpleIngredientByProduct = await _db.Ingredients
            .Where(i => i.LinkedProductId != null && simpleProductIds.Contains(i.LinkedProductId.Value))
            .Select(i => new { ProductId = i.LinkedProductId!.Value, IngredientId = i.Id })
            .ToDictionaryAsync(x => x.ProductId, x => x.IngredientId);

        foreach (var item in items)
        {
            if (!modes.TryGetValue(item.ProductId, out var mode)) continue;

            if (mode == StockTrackingMode.Recipe && recipeByProduct.TryGetValue(item.ProductId, out var lines))
            {
                foreach (var (ingredientId, perPortion) in lines)
                    deltas[ingredientId] = deltas.GetValueOrDefault(ingredientId) + perPortion * item.Quantity;
            }
            else if (mode == StockTrackingMode.Simple && simpleIngredientByProduct.TryGetValue(item.ProductId, out var simpleIngredientId))
            {
                deltas[simpleIngredientId] = deltas.GetValueOrDefault(simpleIngredientId) + item.Quantity;
            }
        }

        return deltas;
    }
}
