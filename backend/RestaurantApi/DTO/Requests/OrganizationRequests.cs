using System.ComponentModel.DataAnnotations;

namespace RestaurantApi.DTOs.Requests;

public class CreateOrganizationRequest
{
    [Required]
    public string Name { get; set; } = string.Empty;
}

public class CreateRestaurantRequest
{
    [Required]
    public string Name { get; set; } = string.Empty;

    public string? Address { get; set; }
    public string? Timezone { get; set; }
    public string? Currency { get; set; }
}

public class CreateInvitationRequest
{
    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;

    /// <summary>Restaurant the invitee will co-own/administer.</summary>
    [Required]
    public Guid RestaurantId { get; set; }
}
