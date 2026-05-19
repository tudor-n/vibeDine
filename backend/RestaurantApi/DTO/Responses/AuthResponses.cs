namespace RestaurantApi.DTOs.Responses;

public class MembershipDto
{
    public Guid OrganizationId { get; set; }
    public string OrganizationName { get; set; } = string.Empty;
    public Guid? RestaurantId { get; set; }
    public string? RestaurantName { get; set; }
    public string Role { get; set; } = string.Empty;
}

public class UserProfileResponse
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public List<MembershipDto> Memberships { get; set; } = [];
}

public class AuthResponse
{
    public string AccessToken { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public string RefreshToken { get; set; } = string.Empty;
    public UserProfileResponse User { get; set; } = new();
}
