namespace RestaurantApi.DTOs.Responses;

public class BulkCreateHierarchyResponse
{
    public List<TempIdMap> CreatedCategories { get; set; } = new();
    public List<TempIdMap> CreatedSubcategories { get; set; } = new();
    public List<CreatedHierarchyProduct> CreatedProducts { get; set; } = new();
}

public class TempIdMap
{
    public string TempId { get; set; } = "";
    public Guid Id { get; set; }
}

public class CreatedHierarchyProduct
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public Guid CategoryId { get; set; }
    public Guid? SubcategoryId { get; set; }
}
