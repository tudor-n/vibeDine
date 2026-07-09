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

    // Ingredients of a recipe-tracked dish (name + per-portion amount + unit). The customer app
    // renders these as a subtle line under the description. Null for non-recipe products.
    public List<MenuRecipeLineResponse>? Recipe { get; set; }
}

// One ingredient line of a recipe-tracked dish, as shown to customers. Mirrors the shared
// `RecipeLine` TS type. `Unit` is the stable wire code (piece|gram|kilogram|milliliter|liter).
public class MenuRecipeLineResponse
{
    public Guid IngredientId { get; set; }
    public string IngredientName { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public decimal QuantityPerPortion { get; set; }
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