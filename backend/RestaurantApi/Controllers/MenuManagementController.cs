using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RestaurantApi.Data;
using RestaurantApi.DTOs.Requests;
using RestaurantApi.DTOs.Responses;
using RestaurantApi.Models;

namespace RestaurantApi.Controllers;

[ApiController]
[Route("api/admin/menu")]
public class MenuManagementController : ControllerBase
{
    private const int NameMaxLength = 120;
    private const int DescriptionMaxLength = 1000;
    private const int ImageUrlMaxLength = 500;
    private const decimal PriceMax = 99999999.99m;

    private readonly AppDbContext _db;
    private readonly ILogger<MenuManagementController> _logger;

    public MenuManagementController(AppDbContext db, ILogger<MenuManagementController> logger)
    {
        _db = db;
        _logger = logger;
    }


    [HttpGet]
    public async Task<IActionResult> GetAdminMenu()
    {
        var categories = await _db.Categories
            .OrderBy(c => c.SortOrder)
            .Select(c => new AdminMenuCategory
            {
                Id = c.Id,
                Name = c.Name,
                SortOrder = c.SortOrder,
                Products = c.Products
                    .OrderBy(p => p.Name)
                    .Select(p => new AdminProductResponse
                    {
                        Id = p.Id,
                        CategoryId = p.CategoryId,
                        CategoryName = c.Name,
                        Name = p.Name,
                        Description = p.Description,
                        Price = p.Price,
                        ImageUrl = p.ImageUrl,
                        IsAvailable = p.IsAvailable ?? false
                    })
                    .ToList()
            })
            .ToListAsync();

        return Ok(new AdminMenuResponse { Categories = categories });
    }


    [HttpGet("categories")]
    public async Task<IActionResult> GetCategories()
    {
        var categories = await _db.Categories
            .OrderBy(c => c.SortOrder)
            .Select(c => new AdminCategoryResponse
            {
                Id = c.Id,
                Name = c.Name,
                SortOrder = c.SortOrder,
                ProductCount = c.Products.Count
            })
            .ToListAsync();

        return Ok(categories);
    }

    [HttpPost("categories")]
    public async Task<IActionResult> CreateCategory([FromBody] CreateCategoryRequest request)
    {
        var name = request.Name?.Trim() ?? string.Empty;
        if (name.Length is < 1 or > NameMaxLength)
            return BadRequest(new { message = $"Name is required and must be 1–{NameMaxLength} characters." });

        var category = new Category
        {
            Id = Guid.NewGuid(),
            Name = name,
            SortOrder = request.SortOrder
        };

        _db.Categories.Add(category);
        await _db.SaveChangesAsync();
        _logger.LogInformation("Created category {Id} ({Name})", category.Id, category.Name);

        var response = new AdminCategoryResponse
        {
            Id = category.Id,
            Name = category.Name,
            SortOrder = category.SortOrder,
            ProductCount = 0
        };

        return CreatedAtAction(nameof(GetCategories), new { }, response);
    }

    [HttpPatch("categories/{id:guid}")]
    public async Task<IActionResult> UpdateCategory(Guid id, [FromBody] UpdateCategoryRequest request)
    {
        var category = await _db.Categories.FindAsync(id);
        if (category == null) return NotFound(new { message = "Category not found." });

        if (request.Name != null)
        {
            var name = request.Name.Trim();
            if (name.Length is < 1 or > NameMaxLength)
                return BadRequest(new { message = $"Name must be 1–{NameMaxLength} characters." });
            category.Name = name;
        }

        if (request.SortOrder.HasValue)
            category.SortOrder = request.SortOrder.Value;

        await _db.SaveChangesAsync();
        _logger.LogInformation("Updated category {Id}", category.Id);

        var productCount = await _db.Products.CountAsync(p => p.CategoryId == category.Id);

        return Ok(new AdminCategoryResponse
        {
            Id = category.Id,
            Name = category.Name,
            SortOrder = category.SortOrder,
            ProductCount = productCount
        });
    }

    [HttpDelete("categories/{id:guid}")]
    public async Task<IActionResult> DeleteCategory(Guid id)
    {
        var category = await _db.Categories.FindAsync(id);
        if (category == null) return NotFound(new { message = "Category not found." });

        var hasProducts = await _db.Products.AnyAsync(p => p.CategoryId == id);
        if (hasProducts)
            return Conflict(new { message = "Category still contains products. Move or remove them first." });

        _db.Categories.Remove(category);
        await _db.SaveChangesAsync();
        _logger.LogInformation("Deleted category {Id}", id);

        return NoContent();
    }


    [HttpGet("products")]
    public async Task<IActionResult> GetProducts(
        [FromQuery] Guid? categoryId = null,
        [FromQuery] bool includeUnavailable = true)
    {
        var query = _db.Products.Include(p => p.Category).AsQueryable();

        if (categoryId.HasValue)
            query = query.Where(p => p.CategoryId == categoryId.Value);

        if (!includeUnavailable)
            query = query.Where(p => p.IsAvailable == true);

        var products = await query
            .OrderBy(p => p.Name)
            .Select(p => new AdminProductResponse
            {
                Id = p.Id,
                CategoryId = p.CategoryId,
                CategoryName = p.Category != null ? p.Category.Name : string.Empty,
                Name = p.Name,
                Description = p.Description,
                Price = p.Price,
                ImageUrl = p.ImageUrl,
                IsAvailable = p.IsAvailable ?? false
            })
            .ToListAsync();

        return Ok(products);
    }

    [HttpGet("products/{id:guid}")]
    public async Task<IActionResult> GetProduct(Guid id)
    {
        var product = await _db.Products
            .Include(p => p.Category)
            .FirstOrDefaultAsync(p => p.Id == id);

        if (product == null) return NotFound(new { message = "Product not found." });

        return Ok(ToAdminProductResponse(product));
    }

    [HttpPost("products")]
    public async Task<IActionResult> CreateProduct([FromBody] CreateProductRequest request)
    {
        var name = request.Name?.Trim() ?? string.Empty;
        if (name.Length is < 1 or > NameMaxLength)
            return BadRequest(new { message = $"Name is required and must be 1–{NameMaxLength} characters." });

        if (request.Price <= 0 || request.Price > PriceMax)
            return BadRequest(new { message = $"Price must be greater than 0 and at most {PriceMax}." });

        if (request.Description != null && request.Description.Length > DescriptionMaxLength)
            return BadRequest(new { message = $"Description must be at most {DescriptionMaxLength} characters." });

        if (!string.IsNullOrWhiteSpace(request.ImageUrl))
        {
            if (request.ImageUrl.Length > ImageUrlMaxLength)
                return BadRequest(new { message = $"ImageUrl must be at most {ImageUrlMaxLength} characters." });
            if (!Uri.TryCreate(request.ImageUrl, UriKind.Absolute, out _))
                return BadRequest(new { message = "ImageUrl must be a valid absolute URL." });
        }

        var category = await _db.Categories.FindAsync(request.CategoryId);
        if (category == null)
            return BadRequest(new { message = "CategoryId does not exist." });

        var product = new Product
        {
            Id = Guid.NewGuid(),
            CategoryId = request.CategoryId,
            Name = name,
            Description = request.Description ?? string.Empty,
            Price = request.Price,
            ImageUrl = string.IsNullOrWhiteSpace(request.ImageUrl) ? null : request.ImageUrl,
            IsAvailable = request.IsAvailable
        };

        _db.Products.Add(product);
        await _db.SaveChangesAsync();
        _logger.LogInformation("Created product {Id} ({Name}) in category {CategoryId}",
            product.Id, product.Name, product.CategoryId);

        var response = new AdminProductResponse
        {
            Id = product.Id,
            CategoryId = product.CategoryId,
            CategoryName = category.Name,
            Name = product.Name,
            Description = product.Description,
            Price = product.Price,
            ImageUrl = product.ImageUrl,
            IsAvailable = product.IsAvailable ?? false
        };

        return CreatedAtAction(nameof(GetProduct), new { id = product.Id }, response);
    }

    [HttpPatch("products/{id:guid}")]
    public async Task<IActionResult> UpdateProduct(Guid id, [FromBody] UpdateProductRequest request)
    {
        var product = await _db.Products
            .Include(p => p.Category)
            .FirstOrDefaultAsync(p => p.Id == id);

        if (product == null) return NotFound(new { message = "Product not found." });

        if (request.Name != null)
        {
            var name = request.Name.Trim();
            if (name.Length is < 1 or > NameMaxLength)
                return BadRequest(new { message = $"Name must be 1–{NameMaxLength} characters." });
            product.Name = name;
        }

        if (request.Description != null)
        {
            if (request.Description.Length > DescriptionMaxLength)
                return BadRequest(new { message = $"Description must be at most {DescriptionMaxLength} characters." });
            product.Description = request.Description;
        }

        if (request.Price.HasValue)
        {
            if (request.Price.Value <= 0 || request.Price.Value > PriceMax)
                return BadRequest(new { message = $"Price must be greater than 0 and at most {PriceMax}." });
            product.Price = request.Price.Value;
        }

        if (request.ImageUrl != null)
        {
            if (request.ImageUrl.Length == 0)
            {
                product.ImageUrl = null;
            }
            else
            {
                if (request.ImageUrl.Length > ImageUrlMaxLength)
                    return BadRequest(new { message = $"ImageUrl must be at most {ImageUrlMaxLength} characters." });
                if (!Uri.TryCreate(request.ImageUrl, UriKind.Absolute, out _))
                    return BadRequest(new { message = "ImageUrl must be a valid absolute URL." });
                product.ImageUrl = request.ImageUrl;
            }
        }

        if (request.CategoryId.HasValue && request.CategoryId.Value != product.CategoryId)
        {
            var newCategory = await _db.Categories.FindAsync(request.CategoryId.Value);
            if (newCategory == null)
                return BadRequest(new { message = "CategoryId does not exist." });
            product.CategoryId = request.CategoryId.Value;
            product.Category = newCategory;
        }

        if (request.IsAvailable.HasValue)
            product.IsAvailable = request.IsAvailable.Value;

        await _db.SaveChangesAsync();
        _logger.LogInformation("Updated product {Id}", product.Id);

        return Ok(ToAdminProductResponse(product));
    }

    [HttpDelete("products/{id:guid}")]
    public async Task<IActionResult> SoftDeleteProduct(Guid id)
    {
        var product = await _db.Products.FindAsync(id);
        if (product == null) return NotFound(new { message = "Product not found." });

        product.IsAvailable = false;
        await _db.SaveChangesAsync();
        _logger.LogInformation("Soft-deleted product {Id}", id);

        return NoContent();
    }

    [HttpPost("products/{id:guid}/restore")]
    public async Task<IActionResult> RestoreProduct(Guid id)
    {
        var product = await _db.Products
            .Include(p => p.Category)
            .FirstOrDefaultAsync(p => p.Id == id);

        if (product == null) return NotFound(new { message = "Product not found." });

        product.IsAvailable = true;
        await _db.SaveChangesAsync();
        _logger.LogInformation("Restored product {Id}", id);

        return Ok(ToAdminProductResponse(product));
    }


    private static AdminProductResponse ToAdminProductResponse(Product p) => new()
    {
        Id = p.Id,
        CategoryId = p.CategoryId,
        CategoryName = p.Category?.Name ?? string.Empty,
        Name = p.Name,
        Description = p.Description,
        Price = p.Price,
        ImageUrl = p.ImageUrl,
        IsAvailable = p.IsAvailable ?? false
    };
}
