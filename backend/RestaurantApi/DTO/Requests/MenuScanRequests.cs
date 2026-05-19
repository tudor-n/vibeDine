namespace RestaurantApi.DTOs.Requests;

public record BulkCreateCategoryItem(
    Guid TempId,
    string Name,
    int? SortOrder = null);

public record BulkCreateProductItem(
    string Name,
    decimal Price,
    Guid CategoryId,
    string? Description = null,
    bool IsAvailable = true);

public record BulkCreateProductsRequest(
    IReadOnlyList<BulkCreateProductItem> Items,
    IReadOnlyList<BulkCreateCategoryItem> NewCategories);
