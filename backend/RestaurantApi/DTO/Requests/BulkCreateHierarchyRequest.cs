namespace RestaurantApi.DTOs.Requests;

public class BulkCreateHierarchyRequest
{
    public List<BulkHierarchyCategory> Categories { get; set; } = new();
    public List<BulkHierarchyProduct> Products { get; set; } = new();
}

public class BulkHierarchyCategory
{
    public string TempId { get; set; } = "";
    public string Name { get; set; } = "";
    public int? SortOrder { get; set; }
    public List<BulkHierarchySubcategory> Subcategories { get; set; } = new();
}

public class BulkHierarchySubcategory
{
    public string TempId { get; set; } = "";
    public string Name { get; set; } = "";
    public int? SortOrder { get; set; }
}

public class BulkHierarchyProduct
{
    public string Name { get; set; } = "";
    public decimal Price { get; set; }
    public string? Description { get; set; }
    public string CategoryTempId { get; set; } = "";
    public string? SubcategoryTempId { get; set; }
    public int SortOrder { get; set; }
    public bool IsAvailable { get; set; } = true;
}
