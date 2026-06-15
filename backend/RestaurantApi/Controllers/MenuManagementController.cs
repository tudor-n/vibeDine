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
[Route("api/restaurants/{restaurantId:guid}/admin/menu")]
public class MenuManagementController : ControllerBase
{
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
                ImageUrl = c.ImageUrl,
                Subcategories = _db.Subcategories
                    .Where(s => s.CategoryId == c.Id)
                    .OrderBy(s => s.SortOrder).ThenBy(s => s.Name)
                    .Select(s => new AdminMenuSubcategory
                    {
                        Id = s.Id,
                        Name = s.Name,
                        SortOrder = s.SortOrder,
                        ImageUrl = s.ImageUrl
                    })
                    .ToList(),
                Products = c.Products
                    .OrderBy(p => p.SortOrder).ThenBy(p => p.Name)
                    .Select(p => new AdminProductResponse
                    {
                        Id = p.Id,
                        CategoryId = p.CategoryId,
                        CategoryName = c.Name,
                        Name = p.Name,
                        Description = p.Description,
                        Price = p.Price,
                        ImageUrl = p.ImageUrl,
                        IsAvailable = p.IsAvailable ?? false,
                        SortOrder = p.SortOrder,
                        SubcategoryId = p.SubcategoryId
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
                ImageUrl = c.ImageUrl,
                ProductCount = c.Products.Count
            })
            .ToListAsync();

        return Ok(categories);
    }

    [HttpPost("categories")]
    public async Task<IActionResult> CreateCategory([FromBody] CreateCategoryRequest request)
    {
        var nameResult = ProductValidator.ValidateName(request.Name);
        if (!nameResult.IsValid) return BadRequest(new { message = nameResult.Error });
        var name = nameResult.Value!;

        var imageUrlResult = ProductValidator.ValidateImageUrl(request.ImageUrl);
        if (!imageUrlResult.IsValid) return BadRequest(new { message = imageUrlResult.Error });
        string? imageUrl = imageUrlResult.Value;

        var category = new Category
        {
            Id = Guid.NewGuid(),
            Name = name,
            SortOrder = request.SortOrder,
            ImageUrl = imageUrl
        };

        _db.Categories.Add(category);
        await _db.SaveChangesAsync();
        _logger.LogInformation("Created category {Id} ({Name})", category.Id, category.Name);

        var response = new AdminCategoryResponse
        {
            Id = category.Id,
            Name = category.Name,
            SortOrder = category.SortOrder,
            ImageUrl = category.ImageUrl,
            ProductCount = 0
        };

        return Created((string?)null, response);
    }

    [HttpPatch("categories/{id:guid}")]
    public async Task<IActionResult> UpdateCategory(Guid id, [FromBody] UpdateCategoryRequest request)
    {
        var category = await _db.Categories.FindAsync(id);
        if (category == null) return NotFound(new { message = "Category not found." });

        if (request.Name != null)
        {
            var nameResult = ProductValidator.ValidateName(request.Name);
            if (!nameResult.IsValid)
                return BadRequest(new { message = $"Name must be 1–{ProductValidator.NameMaxLength} characters." });
            category.Name = nameResult.Value!;
        }

        if (request.SortOrder.HasValue)
            category.SortOrder = request.SortOrder.Value;

        if (request.ImageUrl != null)
        {
            if (request.ImageUrl.Length == 0)
            {
                category.ImageUrl = null;
            }
            else
            {
                var imageUrlResult = ProductValidator.ValidateImageUrl(request.ImageUrl);
                if (!imageUrlResult.IsValid) return BadRequest(new { message = imageUrlResult.Error });
                category.ImageUrl = imageUrlResult.Value;
            }
        }

        await _db.SaveChangesAsync();
        _logger.LogInformation("Updated category {Id}", category.Id);

        var productCount = await _db.Products.CountAsync(p => p.CategoryId == category.Id);

        return Ok(new AdminCategoryResponse
        {
            Id = category.Id,
            Name = category.Name,
            SortOrder = category.SortOrder,
            ImageUrl = category.ImageUrl,
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
            .OrderBy(p => p.SortOrder).ThenBy(p => p.Name)
            .Select(p => new AdminProductResponse
            {
                Id = p.Id,
                CategoryId = p.CategoryId,
                CategoryName = p.Category != null ? p.Category.Name : string.Empty,
                Name = p.Name,
                Description = p.Description,
                Price = p.Price,
                ImageUrl = p.ImageUrl,
                IsAvailable = p.IsAvailable ?? false,
                SortOrder = p.SortOrder
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
        var nameResult = ProductValidator.ValidateName(request.Name);
        if (!nameResult.IsValid) return BadRequest(new { message = nameResult.Error });
        var name = nameResult.Value!;

        var priceResult = ProductValidator.ValidatePrice(request.Price);
        if (!priceResult.IsValid) return BadRequest(new { message = priceResult.Error });

        var descriptionResult = ProductValidator.ValidateDescription(request.Description);
        if (!descriptionResult.IsValid) return BadRequest(new { message = descriptionResult.Error });

        var imageUrlResult = ProductValidator.ValidateImageUrl(request.ImageUrl);
        if (!imageUrlResult.IsValid) return BadRequest(new { message = imageUrlResult.Error });

        var category = await _db.Categories.FindAsync(request.CategoryId);
        if (category == null)
            return BadRequest(new { message = "CategoryId does not exist." });

        // Place new products at the bottom of the category's order.
        var maxSortOrder = await _db.Products
            .Where(p => p.CategoryId == request.CategoryId)
            .Select(p => (int?)p.SortOrder)
            .MaxAsync() ?? -1;

        var product = new Product
        {
            Id = Guid.NewGuid(),
            CategoryId = request.CategoryId,
            Name = name,
            Description = request.Description ?? string.Empty,
            Price = request.Price,
            ImageUrl = imageUrlResult.Value,
            IsAvailable = request.IsAvailable,
            SortOrder = maxSortOrder + 1
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
            IsAvailable = product.IsAvailable ?? false,
            SortOrder = product.SortOrder
        };

        return Created((string?)null, response);
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
            var nameResult = ProductValidator.ValidateName(request.Name);
            if (!nameResult.IsValid)
                return BadRequest(new { message = $"Name must be 1–{ProductValidator.NameMaxLength} characters." });
            product.Name = nameResult.Value!;
        }

        if (request.Description != null)
        {
            var descriptionResult = ProductValidator.ValidateDescription(request.Description);
            if (!descriptionResult.IsValid) return BadRequest(new { message = descriptionResult.Error });
            product.Description = request.Description;
        }

        if (request.Price.HasValue)
        {
            var priceResult = ProductValidator.ValidatePrice(request.Price.Value);
            if (!priceResult.IsValid) return BadRequest(new { message = priceResult.Error });
            product.Price = priceResult.Value;
        }

        if (request.ImageUrl != null)
        {
            if (request.ImageUrl.Length == 0)
            {
                product.ImageUrl = null;
            }
            else
            {
                var imageUrlResult = ProductValidator.ValidateImageUrl(request.ImageUrl);
                if (!imageUrlResult.IsValid) return BadRequest(new { message = imageUrlResult.Error });
                product.ImageUrl = imageUrlResult.Value;
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
        IsAvailable = p.IsAvailable ?? false,
        SortOrder = p.SortOrder
    };

    [HttpPost("products/bulk")]
    public async Task<IActionResult> BulkCreate(
        [FromBody] BulkCreateProductsRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Items.Count == 0)
            return BadRequest(new { message = "At least one item is required." });

        // Validate every item upfront so a roll-back happens before any DB writes.
        var errors = new List<object>();
        for (var i = 0; i < request.Items.Count; i++)
        {
            var item = request.Items[i];
            var nameResult = ProductValidator.ValidateName(item.Name);
            if (!nameResult.IsValid) errors.Add(new { index = i, field = "name", message = nameResult.Error });
            var priceResult = ProductValidator.ValidatePrice(item.Price);
            if (!priceResult.IsValid) errors.Add(new { index = i, field = "price", message = priceResult.Error });
            var descResult = ProductValidator.ValidateDescription(item.Description);
            if (!descResult.IsValid) errors.Add(new { index = i, field = "description", message = descResult.Error });
            var imageResult = ProductValidator.ValidateImageUrl(item.ImageUrl);
            if (!imageResult.IsValid) errors.Add(new { index = i, field = "imageUrl", message = imageResult.Error });
        }
        if (errors.Count > 0) return BadRequest(new { errors });

        await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);

        // 1. Create/merge new categories, building tempId → realId map.
        // Match on normalized name (lowercase + diacritics stripped) on both sides — ILike on its own
        // would miss "Băuturi" vs "BAUTURI" because PostgreSQL ILIKE folds case but not diacritics.
        var allCategories = await _db.Categories.ToListAsync(cancellationToken);
        var categoriesByNormalizedName = allCategories
            .GroupBy(c => NormalizeCategoryName(c.Name))
            .ToDictionary(g => g.Key, g => g.First());
        var tempIdToRealId = new Dictionary<Guid, Guid>();
        foreach (var nc in request.NewCategories)
        {
            var normalized = NormalizeCategoryName(nc.Name);
            if (categoriesByNormalizedName.TryGetValue(normalized, out var existing))
            {
                tempIdToRealId[nc.TempId] = existing.Id;
                continue;
            }
            var nameResult = ProductValidator.ValidateName(nc.Name);
            if (!nameResult.IsValid) return BadRequest(new { errors = new[] { new { field = "newCategories.name", message = nameResult.Error } } });
            var newCat = new Category
            {
                Id = Guid.NewGuid(),
                Name = nameResult.Value!,
                SortOrder = nc.SortOrder,
            };
            _db.Categories.Add(newCat);
            tempIdToRealId[nc.TempId] = newCat.Id;
            categoriesByNormalizedName[normalized] = newCat;
        }
        await _db.SaveChangesAsync(cancellationToken);

        // 2. Resolve each item's CategoryId.
        var resolvedItems = new List<(BulkCreateProductItem Item, Guid RealCategoryId)>();
        var missing = new List<Guid>();
        foreach (var item in request.Items)
        {
            Guid realId;
            if (tempIdToRealId.TryGetValue(item.CategoryId, out var mapped))
            {
                realId = mapped;
            }
            else
            {
                realId = item.CategoryId;
                var exists = await _db.Categories.AnyAsync(c => c.Id == realId, cancellationToken);
                if (!exists) missing.Add(realId);
            }
            resolvedItems.Add((item, realId));
        }
        if (missing.Count > 0)
        {
            await tx.RollbackAsync(cancellationToken);
            return Conflict(new { missingCategoryIds = missing.Distinct().ToList() });
        }

        // 3. Insert products. SortOrder = bottom of the target category.
        var created = new List<AdminProductResponse>();
        var maxSortByCategory = new Dictionary<Guid, int>();
        foreach (var (item, categoryId) in resolvedItems)
        {
            if (!maxSortByCategory.TryGetValue(categoryId, out var max))
            {
                max = await _db.Products
                    .Where(p => p.CategoryId == categoryId)
                    .Select(p => (int?)p.SortOrder)
                    .MaxAsync(cancellationToken) ?? -1;
                maxSortByCategory[categoryId] = max;
            }
            max += 1;
            maxSortByCategory[categoryId] = max;

            var product = new Product
            {
                Id = Guid.NewGuid(),
                CategoryId = categoryId,
                Name = item.Name.Trim(),
                Description = item.Description ?? string.Empty,
                Price = item.Price,
                ImageUrl = ProductValidator.ValidateImageUrl(item.ImageUrl).Value,
                IsAvailable = item.IsAvailable,
                SortOrder = max,
            };
            _db.Products.Add(product);
            created.Add(new AdminProductResponse
            {
                Id = product.Id,
                CategoryId = product.CategoryId,
                CategoryName = "", // populated after the SaveChanges + reload below
                Name = product.Name,
                Description = product.Description,
                Price = product.Price,
                ImageUrl = product.ImageUrl,
                IsAvailable = product.IsAvailable ?? false,
                SortOrder = product.SortOrder,
            });
        }
        await _db.SaveChangesAsync(cancellationToken);

        // 4. Backfill category names for the response.
        var categoryNames = await _db.Categories
            .Where(c => created.Select(r => r.CategoryId).Distinct().Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken);
        foreach (var r in created)
        {
            if (categoryNames.TryGetValue(r.CategoryId, out var name)) r.CategoryName = name;
        }

        await tx.CommitAsync(cancellationToken);
        _logger.LogInformation("Bulk-created {Products} products across {Categories} categories",
            created.Count, created.Select(r => r.CategoryId).Distinct().Count());
        return Ok(created);
    }

    [HttpPost("bulk-create-hierarchy")]
    public async Task<IActionResult> BulkCreateHierarchy(
        [FromBody] BulkCreateHierarchyRequest request,
        CancellationToken ct)
    {
        // Null-guard: System.Text.Json will overwrite the DTO's default `new()` with `null`
        // when the client posts an explicit `null` for these fields.
        var categories = request.Categories ?? new();
        var products = request.Products ?? new();

        if (categories.Count == 0 && products.Count == 0)
            return BadRequest(new { message = "At least one category or product is required." });

        var catTempIds = categories.Select(c => c.TempId).ToList();
        if (catTempIds.Any(string.IsNullOrWhiteSpace))
            return BadRequest(new { message = "Every category needs a tempId." });
        if (catTempIds.Distinct().Count() != catTempIds.Count)
            return BadRequest(new { message = "Duplicate category tempId." });

        var subTempIds = categories.SelectMany(c => (c.Subcategories ?? new()).Select(s => s.TempId)).ToList();
        if (subTempIds.Any(string.IsNullOrWhiteSpace))
            return BadRequest(new { message = "Every subcategory needs a tempId." });
        if (subTempIds.Distinct().Count() != subTempIds.Count)
            return BadRequest(new { message = "Duplicate subcategory tempId." });

        // Category + subcategory name validation runs BEFORE product validation so that
        // a product correctly referencing a category with an invalid name surfaces the
        // category-name error rather than masquerading as a product error.
        foreach (var c in categories)
        {
            var nv = ProductValidator.ValidateName(c.Name);
            if (!nv.IsValid) return BadRequest(new { field = "category.name", message = nv.Error });
            foreach (var s in (c.Subcategories ?? new()))
            {
                var sv = ProductValidator.ValidateName(s.Name);
                if (!sv.IsValid) return BadRequest(new { field = "subcategory.name", message = sv.Error });
            }
        }

        var subToCat = new Dictionary<string, string>();
        foreach (var c in categories)
            foreach (var s in (c.Subcategories ?? new()))
                subToCat[s.TempId] = c.TempId;

        var catTempSet = catTempIds.ToHashSet();
        foreach (var p in products)
        {
            if (!catTempSet.Contains(p.CategoryTempId))
                return BadRequest(new { message = $"Product references unknown categoryTempId '{p.CategoryTempId}'." });
            if (p.SubcategoryTempId is not null)
            {
                if (!subToCat.TryGetValue(p.SubcategoryTempId, out var parent) || parent != p.CategoryTempId)
                    return BadRequest(new { message = $"Subcategory '{p.SubcategoryTempId}' does not belong to category '{p.CategoryTempId}'." });
            }
            var nv = ProductValidator.ValidateName(p.Name);
            if (!nv.IsValid) return BadRequest(new { field = "name", message = nv.Error });
            var pv = ProductValidator.ValidatePrice(p.Price);
            if (!pv.IsValid) return BadRequest(new { field = "price", message = pv.Error });
            var dv = ProductValidator.ValidateDescription(p.Description);
            if (!dv.IsValid) return BadRequest(new { field = "description", message = dv.Error });
            var iv = ProductValidator.ValidateImageUrl(p.ImageUrl);
            if (!iv.IsValid) return BadRequest(new { field = "imageUrl", message = iv.Error });
        }

        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        var catTempToId = new Dictionary<string, Guid>();
        foreach (var c in categories)
        {
            var entity = new Category
            {
                Id = Guid.NewGuid(),
                Name = c.Name.Trim(),
                SortOrder = c.SortOrder
            };
            _db.Categories.Add(entity);
            catTempToId[c.TempId] = entity.Id;
        }
        await _db.SaveChangesAsync(ct);

        var subTempToId = new Dictionary<string, Guid>();
        foreach (var c in categories)
        {
            foreach (var s in (c.Subcategories ?? new()))
            {
                var entity = new Subcategory
                {
                    Id = Guid.NewGuid(),
                    CategoryId = catTempToId[c.TempId],
                    Name = s.Name.Trim(),
                    SortOrder = s.SortOrder
                };
                _db.Subcategories.Add(entity);
                subTempToId[s.TempId] = entity.Id;
            }
        }
        if (subTempToId.Count > 0) await _db.SaveChangesAsync(ct);

        // SortOrder is assigned per (categoryId, subcategoryId) bucket based on input order.
        // The DTO's SortOrder field is ignored — callers who omit it would otherwise collide at 0.
        var sortOrderByBucket = new Dictionary<(Guid CategoryId, Guid? SubcategoryId), int>();
        var createdProducts = new List<CreatedHierarchyProduct>();
        foreach (var p in products)
        {
            var entity = new Product
            {
                Id = Guid.NewGuid(),
                CategoryId = catTempToId[p.CategoryTempId],
                SubcategoryId = p.SubcategoryTempId is null ? null : subTempToId[p.SubcategoryTempId],
                Name = p.Name.Trim(),
                Description = p.Description ?? string.Empty,
                Price = p.Price,
                ImageUrl = ProductValidator.ValidateImageUrl(p.ImageUrl).Value,
                IsAvailable = p.IsAvailable,
            };
            var bucketKey = (entity.CategoryId, entity.SubcategoryId);
            var nextOrder = sortOrderByBucket.GetValueOrDefault(bucketKey, 0);
            entity.SortOrder = nextOrder;
            sortOrderByBucket[bucketKey] = nextOrder + 1;
            _db.Products.Add(entity);
            createdProducts.Add(new CreatedHierarchyProduct
            {
                Id = entity.Id,
                Name = entity.Name,
                CategoryId = entity.CategoryId,
                SubcategoryId = entity.SubcategoryId
            });
        }
        if (createdProducts.Count > 0) await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        _logger.LogInformation(
            "Bulk-created hierarchy: {Categories} categories, {Subcategories} subcategories, {Products} products",
            catTempToId.Count, subTempToId.Count, createdProducts.Count);

        return Ok(new BulkCreateHierarchyResponse
        {
            CreatedCategories = catTempToId.Select(kv => new TempIdMap { TempId = kv.Key, Id = kv.Value }).ToList(),
            CreatedSubcategories = subTempToId.Select(kv => new TempIdMap { TempId = kv.Key, Id = kv.Value }).ToList(),
            CreatedProducts = createdProducts
        });
    }

    private static string NormalizeCategoryName(string raw)
    {
        return new string((raw ?? string.Empty)
            .Trim()
            .Normalize(System.Text.NormalizationForm.FormD)
            .Where(ch => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch)
                != System.Globalization.UnicodeCategory.NonSpacingMark)
            .ToArray())
            .ToLowerInvariant();
    }

    [HttpPost("categories/{categoryId:guid}/products/reorder")]
    public async Task<IActionResult> ReorderProducts(Guid categoryId, [FromBody] ReorderProductsRequest request)
    {
        if (request.ProductIds == null || request.ProductIds.Count == 0)
            return BadRequest(new { message = "productIds must not be empty." });

        if (request.ProductIds.Distinct().Count() != request.ProductIds.Count)
            return BadRequest(new { message = "productIds must not contain duplicates." });

        var category = await _db.Categories.FindAsync(categoryId);
        if (category == null) return NotFound(new { message = "Category not found." });

        var products = await _db.Products
            .Where(p => p.CategoryId == categoryId)
            .ToListAsync();

        var requested = request.ProductIds.ToHashSet();
        var existing = products.Select(p => p.Id).ToHashSet();

        if (!requested.SetEquals(existing))
            return BadRequest(new
            {
                message = "productIds must contain every product in the category, exactly once."
            });

        for (var i = 0; i < request.ProductIds.Count; i++)
        {
            var id = request.ProductIds[i];
            var product = products.First(p => p.Id == id);
            product.SortOrder = i;
        }

        await _db.SaveChangesAsync();
        _logger.LogInformation(
            "Reordered {Count} products in category {CategoryId}",
            request.ProductIds.Count, categoryId);

        return NoContent();
    }
}
