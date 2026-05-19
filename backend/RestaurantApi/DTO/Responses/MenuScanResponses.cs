namespace RestaurantApi.DTOs.Responses;

public record ExtractedMenuItemResponse(
    string Name,
    decimal Price,
    int SourcePage,
    int SourceIndex,
    string? Description = null,
    string? CategoryHint = null,
    double? Confidence = null);

public record FailedPageResponse(int SourcePage, string Reason);

public record MenuScanResponse(
    IReadOnlyList<ExtractedMenuItemResponse> Items,
    IReadOnlyList<FailedPageResponse> FailedPages,
    string Model,
    int SchemaVersion);
