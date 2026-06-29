using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RestaurantApi.Data;
using RestaurantApi.DTOs.Responses;
using RestaurantApi.Models;

namespace RestaurantApi.Controllers;

[ApiController]
[Route("api/restaurants/{restaurantId:guid}/menu")]

public class MenuController: ControllerBase
{
    private readonly AppDbContext _context;

    public MenuController(AppDbContext context)
    {
        _context=context;
    }

   [HttpGet]
   public async Task<IActionResult> GetMenu()
    {
        var outOfStock = await ComputeOutOfStockProductsAsync();

        // IsAvailable != false keeps staff-hidden products out while STILL returning
        // out-of-stock ones, which the customer app renders blurred ("Indisponibil momentan").
        var categories = await _context.Categories
        .Where(c => c.Products.Any(p => p.IsAvailable != false))
        .OrderBy(p=>p.SortOrder)
        .Select(category => new MenuCategoryResponse
        {
            Id=category.Id,
            Name=category.Name,
            SortOrder=category.SortOrder?? 0,
            Subcategories = _context.Subcategories
                .Where(s => s.CategoryId == category.Id)
                .OrderBy(s => s.SortOrder).ThenBy(s => s.Name)
                .Select(s => new MenuSubcategoryResponse
                {
                    Id = s.Id,
                    Name = s.Name,
                    SortOrder = s.SortOrder,
                    ImageUrl = s.ImageUrl
                })
                .ToList(),
            Products=category.Products

            .Where(product => product.IsAvailable != false)
            .OrderBy(product => product.SortOrder).ThenBy(product => product.Name)
            .Select(product=>new MenuProductResponse
            {
                Id=product.Id,
                Name=product.Name,
                Description= product.Description,
                Price=product.Price,
                ImageUrl=product.ImageUrl,
                SubcategoryId = product.SubcategoryId
            }).ToList()
        }).ToListAsync();

        ApplyAvailability(categories.SelectMany(c => c.Products), outOfStock);

        return Ok(new { categories });
    }

    [HttpGet("{categoryId:guid}")]

    public async Task<IActionResult> GetMenuByCategory(Guid categoryId)
    {
        var categoryExists = await _context.Categories.AnyAsync(c => c.Id == categoryId);
        if (!categoryExists)
        {
            return NotFound(new { message = "Category not found." });
        }

        var outOfStock = await ComputeOutOfStockProductsAsync();

        var products = await _context.Products
        .Where(p=> p.CategoryId == categoryId && p.IsAvailable != false)
        .OrderBy(product => product.SortOrder).ThenBy(product => product.Name)
        .Select(product => new MenuProductResponse
        {
            Id=product.Id,
            Name=product.Name,
            Description=product.Description,
            Price=product.Price,
            ImageUrl=product.ImageUrl,
            SubcategoryId = product.SubcategoryId
        })
        .ToListAsync();

        ApplyAvailability(products, outOfStock);

        return Ok(products);
    }

    // Sets the combined IsAvailable / OutOfStock flags on already-fetched products. Every product
    // reaching here is staff-visible (the queries filter IsAvailable != false), so orderability
    // hinges purely on stock.
    private static void ApplyAvailability(IEnumerable<MenuProductResponse> products, HashSet<Guid> outOfStock)
    {
        foreach (var product in products)
        {
            product.OutOfStock = outOfStock.Contains(product.Id);
            product.IsAvailable = !product.OutOfStock;
        }
    }

    // The set of products that cannot currently be made: a recipe product whose any ingredient
    // can't cover one portion, or a simple product whose stock is below one unit. Untracked
    // products (and recipe products with no lines) never appear here.
    private async Task<HashSet<Guid>> ComputeOutOfStockProductsAsync()
    {
        var ingredientQty = await _context.Ingredients
            .Where(i => i.LinkedProductId == null)
            .ToDictionaryAsync(i => i.Id, i => i.QuantityOnHand);

        var outOfStock = new HashSet<Guid>();

        var recipeLines = await _context.RecipeItems
            .Select(r => new { r.ProductId, r.IngredientId, r.QuantityPerPortion })
            .ToListAsync();
        foreach (var grp in recipeLines.GroupBy(r => r.ProductId))
        {
            var cannotMake = grp.Any(line =>
                ingredientQty.GetValueOrDefault(line.IngredientId, 0m) < line.QuantityPerPortion);
            if (cannotMake) outOfStock.Add(grp.Key);
        }

        var simpleLinks = await _context.Ingredients
            .Where(i => i.LinkedProductId != null)
            .Select(i => new { ProductId = i.LinkedProductId!.Value, i.QuantityOnHand })
            .ToListAsync();
        foreach (var s in simpleLinks)
        {
            if (s.QuantityOnHand < 1m) outOfStock.Add(s.ProductId);
        }

        return outOfStock;
    }
}

