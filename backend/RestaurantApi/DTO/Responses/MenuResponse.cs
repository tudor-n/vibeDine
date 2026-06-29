namespace RestaurantApi.DTOs.Responses;

public class MenuCategoryResponse
{
    public Guid Id {get; set;}
    public string Name {get;set;}=string.Empty;
    public int SortOrder{get;set;}
    public List<MenuSubcategoryResponse> Subcategories { get; set; } = new();
    public List<MenuProductResponse> Products {get; set;} = [];
}

public class MenuSubcategoryResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public int? SortOrder { get; set; }
    public string? ImageUrl { get; set; }
}

public class MenuProductResponse
{
    public Guid Id {get; set;}
    public string Name{get;set;}=string.Empty;
    public string? Description {get;set;}
    public decimal Price{get;set;}
    public string? ImageUrl{get;set;}
    public Guid? SubcategoryId { get; set; }

    // Combined orderable flag: visible to staff AND stock can cover a portion (or untracked).
    public bool IsAvailable { get; set; } = true;
    // True only when the product is stock-tracked and cannot currently be made — drives the
    // "Indisponibil momentan" blur on the customer menu (distinct from a staff-hidden product,
    // which is omitted from the menu entirely).
    public bool OutOfStock { get; set; }
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
    public Guid? SubcategoryId { get; set; }
}

public class AdminMenuSubcategory
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int? SortOrder { get; set; }
    public string? ImageUrl { get; set; }
}

public class AdminMenuCategory
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int? SortOrder { get; set; }
    public string? ImageUrl { get; set; }
    public List<AdminMenuSubcategory> Subcategories { get; set; } = new();
    public List<AdminProductResponse> Products { get; set; } = new();
}

public class AdminMenuResponse
{
    public List<AdminMenuCategory> Categories { get; set; } = new();
}