using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RestaurantApi.Data;
using RestaurantApi.DTOs.Responses;

namespace RestaurantApi.Controllers;

[ApiController]
[Route("api/[controller]")]

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
        var availableProducts = await _context.Categories
        .Where(c => c.Products.Any(p => p.IsAvailable == true))
        .OrderBy(p=>p.SortOrder)
        .Select(category => new MenuCategoryResponse
        {
            Id=category.Id,
            Name=category.Name,
            SortOrder=category.SortOrder?? 0,
            Products=category.Products

            .Where(product => product.IsAvailable==true)
            .OrderBy(product => product.SortOrder).ThenBy(product => product.Name)
            .Select(product=>new MenuProductResponse
            {
                Id=product.Id,
                Name=product.Name,
                Description= product.Description,
                Price=product.Price,
                ImageUrl=product.ImageUrl
            }).ToList()
        }).ToListAsync();
  
     return Ok(new { categories = availableProducts });
    }

    [HttpGet("{categoryId:guid}")]

    public async Task<IActionResult> GetMenuByCategory(Guid categoryId)
    {
        var categoryExists = await _context.Categories.AnyAsync(c => c.Id == categoryId);
        if (!categoryExists)
        {
            return NotFound(new { message = "Category not found." });
        }

        var products = await _context.Products
        .Where(p=> p.CategoryId == categoryId && p.IsAvailable==true)
        .OrderBy(product => product.SortOrder).ThenBy(product => product.Name)
        .Select(product => new MenuProductResponse
        {
            Id=product.Id,
            Name=product.Name,
            Description=product.Description,
            Price=product.Price,
            ImageUrl=product.ImageUrl
        })
        .ToListAsync();

        return Ok(products);
    }

}

