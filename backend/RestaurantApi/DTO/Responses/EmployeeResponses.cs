namespace RestaurantApi.DTOs.Responses;

public class EmployeeResponse
{
    public Guid MembershipId { get; set; }
    public Guid UserId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
    public string Role { get; set; } = string.Empty;
    public Guid? RestaurantId { get; set; }
    public string? RestaurantName { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
