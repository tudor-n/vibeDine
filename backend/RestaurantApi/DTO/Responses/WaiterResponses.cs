namespace RestaurantApi.DTOs.Responses;

public class WaiterSignupLinkResponse
{
    public Guid Token { get; set; }
    public string SignupPath { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class WaiterSignupPreviewResponse
{
    public string OrganizationName { get; set; } = string.Empty;
    public string RestaurantName { get; set; } = string.Empty;
}

public class WaiterSignupResultResponse
{
    /// <summary>"confirmation_sent" — the waiter must confirm email, then wait for manager approval.</summary>
    public string Status { get; set; } = "confirmation_sent";
    public string Email { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

public class WaiterRowResponse
{
    public Guid MembershipId { get; set; }
    public Guid UserId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
    public Guid RestaurantId { get; set; }
    public string RestaurantName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public bool EmailConfirmed { get; set; }
}
