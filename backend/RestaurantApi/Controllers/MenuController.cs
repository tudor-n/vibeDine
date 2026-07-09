using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RestaurantApi.Data;
using RestaurantApi.DTOs.Responses;
using RestaurantApi.Models;
using RestaurantApi.Services;

namespace RestaurantApi.Controllers;

[ApiController]
[Route("api/restaurants/{restaurantId:guid}/menu")]

public class MenuController: ControllerBase
{
    private readonly AppDbContext _context;
    private readonly IStockService _stockService;

    public MenuController(AppDbContext context, IStockService stockService)
    {
        _context=context;
        _stockService = stockService;
    }

   [HttpGet]
   public async Task<IActionResult> GetMenu()
    {
        var outOfStock = await _stockService.GetOutOfStockProductsAsync();

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
        await ApplyRecipesAsync(categories.SelectMany(c => c.Products));

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

        var outOfStock = await _stockService.GetOutOfStockProductsAsync();

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
        await ApplyRecipesAsync(products);

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

    // Loads recipe ingredients for the recipe-tracked products in the given set and hangs them
    // off each product response. Done as a separate pass (not in the menu projection) so the
    // enum→wire-code unit mapping runs in memory rather than needing EF SQL translation.
    private async Task ApplyRecipesAsync(IEnumerable<MenuProductResponse> products)
    {
        var byId = products.ToDictionary(p => p.Id);
        if (byId.Count == 0) return;

        var ids = byId.Keys.ToList();
        var lines = await _context.RecipeItems
            .Where(ri => ri.Product.StockTrackingMode == StockTrackingMode.Recipe && ids.Contains(ri.ProductId))
            .OrderBy(ri => ri.Ingredient.Name)
            .Select(ri => new
            {
                ri.ProductId,
                ri.IngredientId,
                IngredientName = ri.Ingredient.Name,
                ri.Ingredient.Unit,
                ri.QuantityPerPortion,
            })
            .ToListAsync();

        foreach (var group in lines.GroupBy(l => l.ProductId))
        {
            if (!byId.TryGetValue(group.Key, out var product)) continue;
            product.Recipe = group.Select(l => new MenuRecipeLineResponse
            {
                IngredientId = l.IngredientId,
                IngredientName = l.IngredientName,
                Unit = StockUnits.ToCode(l.Unit),
                QuantityPerPortion = l.QuantityPerPortion,
            }).ToList();
        }
    }
}

