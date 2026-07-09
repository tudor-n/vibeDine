using System.ComponentModel.DataAnnotations;

namespace PlatformApi.DTO.Requests;

public class LoginRequest
{
    [Required, EmailAddress] public string Email { get; set; } = string.Empty;
    [Required] public string Password { get; set; } = string.Empty;
}

public class CreateOrganizationRequest
{
    [Required] public string Name { get; set; } = string.Empty;
    public string? Slug { get; set; }
}

public class UpdateOrganizationRequest
{
    public string? Name { get; set; }
    public string? Slug { get; set; }
}

public class CreateLocationRequest
{
    [Required] public Guid OrganizationId { get; set; }
    [Required] public string Name { get; set; } = string.Empty;
    public string? Address { get; set; }
    public string? Timezone { get; set; }
    public string? Currency { get; set; }
    public string? Locale { get; set; }
}

public class UpdateLocationRequest
{
    public string? Name { get; set; }
    public string? Address { get; set; }
    public string? Timezone { get; set; }
    public string? Currency { get; set; }
    public string? Locale { get; set; }
}

public class UpdateLocationStatusRequest
{
    [Required] public bool IsActive { get; set; }
}

public class UpdateMemberStatusRequest
{
    /// <summary>"active" | "rejected" | "pending_approval".</summary>
    [Required] public string Status { get; set; } = string.Empty;
}

public class AssignAdminRequest
{
    [Required, EmailAddress] public string Email { get; set; } = string.Empty;
}

public class CreateRegistrationLinkRequest
{
    /// <summary>"new_organization" | "new_location".</summary>
    [Required] public string Type { get; set; } = string.Empty;
    public Guid? OrganizationId { get; set; }
    public string? Label { get; set; }
    public string? IntendedEmail { get; set; }
    public int? ExpiresInDays { get; set; }
}
