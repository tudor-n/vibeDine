namespace RestaurantApi.DTOs.Responses;

public class MenuCategoryResponse
{
    public Guid Id {get; set;}
    public string Name {get;set;}=string.Empty;
    public int SortOrder{get;set;}
    public List<MenuProductResponse> Products {get; set;} = [];
}

public class MenuProductResponse
{
    public Guid Id {get; set;}
    public string Name{get;set;}=string.Empty;
    public string? Description {get;set;}
    public decimal Price{get;set;}
    public string? ImageUrl{get;set;}
}

public class AdminCategoryResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int? SortOrder { get; set; }
    public string? ImageUrl { get; set; }
    public int ProductCount { get; set; }
}

public class AdminProductResponse
{
    public Guid Id { get; set; }
    public Guid CategoryId { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal Price { get; set; }
    public string? ImageUrl { get; set; }
    public bool IsAvailable { get; set; }
    public int SortOrder { get; set; }
}

public class AdminMenuCategory
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int? SortOrder { get; set; }
    public string? ImageUrl { get; set; }
    public List<AdminProductResponse> Products { get; set; } = new();
}

public class AdminMenuResponse
{
    public List<AdminMenuCategory> Categories { get; set; } = new();
}