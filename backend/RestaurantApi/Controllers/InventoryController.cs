using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RestaurantApi.Data;
using RestaurantApi.DTOs.Requests;
using RestaurantApi.DTOs.Responses;
using RestaurantApi.Models;
using RestaurantApi.Validators;

namespace RestaurantApi.Controllers;

[ApiController]
[Authorize]
[Route("api/restaurants/{restaurantId:guid}/admin/inventory")]
public class InventoryController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ILogger<InventoryController> _logger;

    public InventoryController(AppDbContext db, ILogger<InventoryController> logger)
    {
        _db = db;
        _logger = logger;
    }

    // ── Ingredients (stock items) ────────────────────────────────────────────

    [HttpGet("ingredients")]
    public async Task<IActionResult> GetIngredients(
        [FromQuery] bool lowStockOnly = false,
        [FromQuery] bool includeLinked = true)
    {
        var raw = await _db.Ingredients
            .Where(i => includeLinked || i.LinkedProductId == null)
            .Where(i => !lowStockOnly || i.QuantityOnHand <= i.LowStockThreshold)
            .Select(i => new
            {
                i.Id,
                i.Name,
                i.Unit,
                i.QuantityOnHand,
                i.LowStockThreshold,
                i.LinkedProductId,
                LinkedProductName = i.LinkedProduct != null ? i.LinkedProduct.Name : null,
            })
            .ToListAsync();

        var list = raw
            .Select(i => new AdminIngredientResponse
            {
                Id = i.Id,
                Name = i.Name,
                Unit = StockUnits.ToCode(i.Unit),
                QuantityOnHand = i.QuantityOnHand,
                LowStockThreshold = i.LowStockThreshold,
                IsLow = i.QuantityOnHand <= i.LowStockThreshold,
                LinkedProductId = i.LinkedProductId,
                LinkedProductName = i.LinkedProductName,
            })
            .OrderBy(i => i.Name)
            .ToList();

        return Ok(list);
    }

    [HttpPost("ingredients")]
    public async Task<IActionResult> CreateIngredient([FromBody] CreateIngredientRequest request)
    {
        var nameResult = InventoryValidator.ValidateName(request.Name);
        if (!nameResult.IsValid) return BadRequest(new { message = nameResult.Error });

        if (!StockUnits.TryFromCode(request.Unit, out var unit))
            return BadRequest(new { message = "Unitate de măsură invalidă." });

        var qtyResult = InventoryValidator.ValidateQuantity(request.QuantityOnHand, "Cantitatea");
        if (!qtyResult.IsValid) return BadRequest(new { message = qtyResult.Error });

        var thresholdResult = InventoryValidator.ValidateQuantity(request.LowStockThreshold, "Pragul de alertă");
        if (!thresholdResult.IsValid) return BadRequest(new { message = thresholdResult.Error });

        var ingredient = new Ingredient
        {
            Id = Guid.NewGuid(),
            Name = nameResult.Value!,
            Unit = unit,
            QuantityOnHand = request.QuantityOnHand,
            LowStockThreshold = request.LowStockThreshold,
        };

        _db.Ingredients.Add(ingredient);
        await _db.SaveChangesAsync();
        _logger.LogInformation("Created ingredient {Id} ({Name})", ingredient.Id, ingredient.Name);

        return Created((string?)null, ToResponse(ingredient, null));
    }

    [HttpPatch("ingredients/{id:guid}")]
    public async Task<IActionResult> UpdateIngredient(Guid id, [FromBody] UpdateIngredientRequest request)
    {
        var ingredient = await _db.Ingredients.FirstOrDefaultAsync(i => i.Id == id);
        if (ingredient == null) return NotFound(new { message = "Ingredientul nu a fost găsit." });

        if (ingredient.LinkedProductId != null)
            return Conflict(new { message = "Acest stoc aparține unui produs simplu. Modifică-l din meniu." });

        if (request.Name != null)
        {
            var nameResult = InventoryValidator.ValidateName(request.Name);
            if (!nameResult.IsValid) return BadRequest(new { message = nameResult.Error });
            ingredient.Name = nameResult.Value!;
        }

        if (request.Unit != null)
        {
            if (!StockUnits.TryFromCode(request.Unit, out var unit))
                return BadRequest(new { message = "Unitate de măsură invalidă." });
            ingredient.Unit = unit;
        }

        if (request.LowStockThreshold.HasValue)
        {
            var thresholdResult = InventoryValidator.ValidateQuantity(request.LowStockThreshold.Value, "Pragul de alertă");
            if (!thresholdResult.IsValid) return BadRequest(new { message = thresholdResult.Error });
            ingredient.LowStockThreshold = request.LowStockThreshold.Value;
        }

        await _db.SaveChangesAsync();
        _logger.LogInformation("Updated ingredient {Id}", ingredient.Id);

        return Ok(ToResponse(ingredient, null));
    }

    [HttpPost("ingredients/{id:guid}/adjust")]
    public async Task<IActionResult> AdjustIngredient(Guid id, [FromBody] AdjustStockRequest request)
    {
        var exists = await _db.Ingredients.AnyAsync(i => i.Id == id);
        if (!exists) return NotFound(new { message = "Ingredientul nu a fost găsit." });

        if (request.SetTo is { } setTo)
        {
            if (setTo < 0) return BadRequest(new { message = "Cantitatea nu poate fi negativă." });
            // Absolute set: the manager is declaring the true count.
            await _db.Ingredients.Where(i => i.Id == id)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.QuantityOnHand, setTo));
        }
        else if (request.Delta is { } delta)
        {
            // Atomic relative change so a concurrent order decrement can't be lost.
            await _db.Ingredients.Where(i => i.Id == id)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.QuantityOnHand, x => x.QuantityOnHand + delta));
        }
        else
        {
            return BadRequest(new { message = "Specifică „delta” sau „setTo”." });
        }

        _logger.LogInformation("Adjusted stock for ingredient {Id}", id);
        return Ok(await LoadResponseAsync(id));
    }

    [HttpDelete("ingredients/{id:guid}")]
    public async Task<IActionResult> DeleteIngredient(Guid id)
    {
        var ingredient = await _db.Ingredients.FirstOrDefaultAsync(i => i.Id == id);
        if (ingredient == null) return NotFound(new { message = "Ingredientul nu a fost găsit." });

        if (ingredient.LinkedProductId != null)
            return Conflict(new { message = "Acest stoc aparține unui produs simplu. Schimbă produsul pe „fără urmărire” din meniu." });

        var usedInRecipe = await _db.RecipeItems.AnyAsync(r => r.IngredientId == id);
        if (usedInRecipe)
            return Conflict(new { message = "Ingredientul e folosit în rețete. Elimină-l din rețete mai întâi." });

        _db.Ingredients.Remove(ingredient);
        await _db.SaveChangesAsync();
        _logger.LogInformation("Deleted ingredient {Id}", id);

        return NoContent();
    }

    // ── Per-product recipe / stock mode ──────────────────────────────────────

    [HttpGet("products/{productId:guid}/stock")]
    public async Task<IActionResult> GetProductStock(Guid productId)
    {
        var exists = await _db.Products.AnyAsync(p => p.Id == productId);
        if (!exists) return NotFound(new { message = "Produsul nu a fost găsit." });

        return Ok(await BuildProductStockResponseAsync(productId));
    }

    [HttpPut("products/{productId:guid}/stock")]
    public async Task<IActionResult> SetProductStock(Guid productId, [FromBody] SetProductStockRequest request)
    {
        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == productId);
        if (product == null) return NotFound(new { message = "Produsul nu a fost găsit." });

        if (!TryParseMode(request.Mode, out var mode))
            return BadRequest(new { message = "Mod de urmărire invalid." });

        await using var tx = await _db.Database.BeginTransactionAsync();

        // Clear any existing recipe lines; re-added below only for recipe mode.
        var existingRecipe = await _db.RecipeItems.Where(r => r.ProductId == productId).ToListAsync();
        if (existingRecipe.Count > 0) _db.RecipeItems.RemoveRange(existingRecipe);

        // The 1:1 stock row, if this product is currently simple-tracked.
        var linked = await _db.Ingredients.FirstOrDefaultAsync(i => i.LinkedProductId == productId);

        if (mode == StockTrackingMode.Recipe)
        {
            var lines = request.Recipe ?? new();

            // Only standalone ingredients can appear in a recipe (never another product's auto-row).
            var standaloneIds = (await _db.Ingredients
                .Where(i => i.LinkedProductId == null)
                .Select(i => i.Id)
                .ToListAsync()).ToHashSet();

            var seen = new HashSet<Guid>();
            foreach (var line in lines)
            {
                if (!standaloneIds.Contains(line.IngredientId))
                    return BadRequest(new { message = "Rețeta conține un ingredient inexistent." });
                if (!seen.Add(line.IngredientId))
                    return BadRequest(new { message = "Un ingredient apare de două ori în rețetă." });
                var perPortion = InventoryValidator.ValidatePerPortion(line.QuantityPerPortion);
                if (!perPortion.IsValid) return BadRequest(new { message = perPortion.Error });
            }

            foreach (var line in lines)
            {
                _db.RecipeItems.Add(new RecipeItem
                {
                    Id = Guid.NewGuid(),
                    ProductId = productId,
                    IngredientId = line.IngredientId,
                    QuantityPerPortion = line.QuantityPerPortion,
                });
            }

            if (linked != null) _db.Ingredients.Remove(linked); // leaving simple mode
            product.StockTrackingMode = StockTrackingMode.Recipe;
        }
        else if (mode == StockTrackingMode.Simple)
        {
            if (!StockUnits.TryFromCode(request.SimpleUnit, out var unit))
                return BadRequest(new { message = "Unitate de măsură invalidă." });

            var qty = request.SimpleQuantity ?? 0m;
            var qtyResult = InventoryValidator.ValidateQuantity(qty, "Cantitatea");
            if (!qtyResult.IsValid) return BadRequest(new { message = qtyResult.Error });

            var threshold = request.SimpleThreshold ?? 0m;
            var thresholdResult = InventoryValidator.ValidateQuantity(threshold, "Pragul de alertă");
            if (!thresholdResult.IsValid) return BadRequest(new { message = thresholdResult.Error });

            if (linked == null)
            {
                _db.Ingredients.Add(new Ingredient
                {
                    Id = Guid.NewGuid(),
                    Name = product.Name,
                    Unit = unit,
                    QuantityOnHand = qty,
                    LowStockThreshold = threshold,
                    LinkedProductId = productId,
                });
            }
            else
            {
                linked.Name = product.Name;
                linked.Unit = unit;
                linked.QuantityOnHand = qty;
                linked.LowStockThreshold = threshold;
            }

            product.StockTrackingMode = StockTrackingMode.Simple;
        }
        else // untracked
        {
            if (linked != null) _db.Ingredients.Remove(linked);
            product.StockTrackingMode = StockTrackingMode.Untracked;
        }

        await _db.SaveChangesAsync();
        await tx.CommitAsync();
        _logger.LogInformation("Set stock mode {Mode} for product {ProductId}", request.Mode, productId);

        return Ok(await BuildProductStockResponseAsync(productId));
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static AdminIngredientResponse ToResponse(Ingredient i, string? linkedProductName) => new()
    {
        Id = i.Id,
        Name = i.Name,
        Unit = StockUnits.ToCode(i.Unit),
        QuantityOnHand = i.QuantityOnHand,
        LowStockThreshold = i.LowStockThreshold,
        IsLow = i.QuantityOnHand <= i.LowStockThreshold,
        LinkedProductId = i.LinkedProductId,
        LinkedProductName = linkedProductName,
    };

    private async Task<AdminIngredientResponse> LoadResponseAsync(Guid id)
    {
        var row = await _db.Ingredients
            .AsNoTracking()
            .Where(i => i.Id == id)
            .Select(i => new
            {
                i.Id,
                i.Name,
                i.Unit,
                i.QuantityOnHand,
                i.LowStockThreshold,
                i.LinkedProductId,
                LinkedProductName = i.LinkedProduct != null ? i.LinkedProduct.Name : null,
            })
            .FirstAsync();

        return new AdminIngredientResponse
        {
            Id = row.Id,
            Name = row.Name,
            Unit = StockUnits.ToCode(row.Unit),
            QuantityOnHand = row.QuantityOnHand,
            LowStockThreshold = row.LowStockThreshold,
            IsLow = row.QuantityOnHand <= row.LowStockThreshold,
            LinkedProductId = row.LinkedProductId,
            LinkedProductName = row.LinkedProductName,
        };
    }

    private async Task<ProductStockResponse> BuildProductStockResponseAsync(Guid productId)
    {
        var product = await _db.Products.AsNoTracking().FirstAsync(p => p.Id == productId);
        var resp = new ProductStockResponse
        {
            ProductId = productId,
            Mode = ModeToString(product.StockTrackingMode),
        };

        if (product.StockTrackingMode == StockTrackingMode.Recipe)
        {
            var rawLines = await _db.RecipeItems
                .AsNoTracking()
                .Where(r => r.ProductId == productId)
                .Select(r => new { r.IngredientId, r.Ingredient.Name, r.Ingredient.Unit, r.QuantityPerPortion })
                .ToListAsync();

            resp.Recipe = rawLines
                .Select(l => new RecipeLineResponse
                {
                    IngredientId = l.IngredientId,
                    IngredientName = l.Name,
                    Unit = StockUnits.ToCode(l.Unit),
                    QuantityPerPortion = l.QuantityPerPortion,
                })
                .OrderBy(l => l.IngredientName)
                .ToList();
        }
        else if (product.StockTrackingMode == StockTrackingMode.Simple)
        {
            var linked = await _db.Ingredients
                .AsNoTracking()
                .FirstOrDefaultAsync(i => i.LinkedProductId == productId);
            if (linked != null)
            {
                resp.SimpleIngredientId = linked.Id;
                resp.SimpleQuantity = linked.QuantityOnHand;
                resp.SimpleUnit = StockUnits.ToCode(linked.Unit);
                resp.SimpleThreshold = linked.LowStockThreshold;
            }
        }

        return resp;
    }

    private static string ModeToString(StockTrackingMode mode) => mode switch
    {
        StockTrackingMode.Recipe => "recipe",
        StockTrackingMode.Simple => "simple",
        _ => "untracked",
    };

    private static bool TryParseMode(string? raw, out StockTrackingMode mode)
    {
        switch ((raw ?? string.Empty).Trim().ToLowerInvariant())
        {
            case "recipe": mode = StockTrackingMode.Recipe; return true;
            case "simple": mode = StockTrackingMode.Simple; return true;
            case "untracked": mode = StockTrackingMode.Untracked; return true;
            default: mode = StockTrackingMode.Untracked; return false;
        }
    }
}
