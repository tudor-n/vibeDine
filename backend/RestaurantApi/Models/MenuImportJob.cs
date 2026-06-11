namespace RestaurantApi.Models;

public class MenuImportJob : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RestaurantId { get; set; }
    public string Status { get; set; } = "pending"; // pending | extracting | complete | failed | cancelled
    public string Phase { get; set; } = "parsing";  // parsing | classifying | extracting | finalizing
    public int TotalPages { get; set; }
    public int ProcessedPages { get; set; }
    public int FailedPages { get; set; }
    public string StorageKey { get; set; } = null!;
    public string OriginalFileName { get; set; } = null!;
    public long FileSizeBytes { get; set; }
    public string ProviderUsed { get; set; } = "gemini";
    public string? ResultJson { get; set; }  // jsonb in Postgres
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public Guid? LeaseId { get; set; }
    public DateTime? LeaseExpiresAt { get; set; }
    public DateTime? LastHeartbeatAt { get; set; }
    public int ProcessingAttempts { get; set; }
}
