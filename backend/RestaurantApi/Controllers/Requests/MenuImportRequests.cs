namespace RestaurantApi.Controllers.Requests;

public sealed class StartPdfImportResponse
{
    public Guid JobId { get; set; }
}

public sealed class MenuImportJobResponse
{
    public Guid Id { get; set; }
    public string Status { get; set; } = "";
    public string Phase { get; set; } = "";
    public int TotalPages { get; set; }
    public int ProcessedPages { get; set; }
    public int FailedPages { get; set; }
    public string OriginalFileName { get; set; } = "";
    public long FileSizeBytes { get; set; }
    public string ProviderUsed { get; set; } = "";
    // Only populated when Status == "complete"
    public object? Result { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}

public sealed class ProgressCallbackRequest
{
    public Guid JobId { get; set; }
    public string Status { get; set; } = "";
    public string Phase { get; set; } = "";
    public int ProcessedPages { get; set; }
    public int TotalPages { get; set; }
    public int FailedPages { get; set; }
    public double? EtaSeconds { get; set; }
    public string? ErrorMessage { get; set; }
}
