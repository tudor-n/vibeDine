namespace RestaurantApi.DTOs.Responses;

public class OrganizationResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public class RestaurantResponse
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? Address { get; set; }
    public string Timezone { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public bool IsActive { get; set; }
}

public class InvitationResponse
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public Guid? RestaurantId { get; set; }
    public string Role { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public Guid Token { get; set; }
    public DateTime ExpiresAt { get; set; }
    /// <summary>Relative accept path the frontend turns into a copyable link.</summary>
    public string AcceptPath { get; set; } = string.Empty;
}
