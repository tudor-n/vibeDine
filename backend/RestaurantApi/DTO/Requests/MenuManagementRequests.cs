namespace RestaurantApi.DTOs.Requests;

public class CreateCategoryRequest
{
    public string Name { get; set; } = string.Empty;
    public int? SortOrder { get; set; }
    public string? ImageUrl { get; set; }
}

public class UpdateCategoryRequest
{
    public string? Name { get; set; }
    public int? SortOrder { get; set; }
    public string? ImageUrl { get; set; }
}

public class CreateProductRequest
{
    public Guid CategoryId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal Price { get; set; }
    public string? ImageUrl { get; set; }
    public bool IsAvailable { get; set; } = true;
}

public class UpdateProductRequest
{
    public Guid? CategoryId { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public decimal? Price { get; set; }
    public string? ImageUrl { get; set; }
    public bool? IsAvailable { get; set; }
}

public class ReorderProductsRequest
{
    public List<Guid> ProductIds { get; set; } = new();
}
