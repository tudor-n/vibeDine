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
            .Select(product=>new MenuProductResponse
            {
                Id=product.Id,
                Name=product.Name,
                Description= product.Description,
                Price=product.Price,
                ImageUrl=product.ImageUrl
            }).ToList()
        }).ToListAsync();
  
     return Ok(availableProducts);
    }

    [HttpGet("{categoryId:guid}")]

    public async Task<IActionResult> GetMenuByCategory(Guid categoryId)
    {
        var products = await _context.Products
        .Where(p=> p.CategoryId == categoryId && p.IsAvailable==true)
        .Select(product => new MenuProductResponse
        {
            Id=product.Id,
            Name=product.Name,
            Description=product.Description,
            Price=product.Price,
            ImageUrl=product.ImageUrl
        })
        .ToListAsync();

        if(products.Count==0)
        {
            return NotFound(new {message = "No available products found for this category."});
        }
       
        return Ok(products);
    }

}

