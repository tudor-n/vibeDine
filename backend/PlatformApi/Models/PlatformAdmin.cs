namespace PlatformApi.Models;

/// <summary>
/// An operator of the platform console (your team). Fully decoupled from restaurant users.
/// Lives in the HQ-owned <c>platform_admins</c> table.
/// </summary>
public class PlatformAdmin
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginAt { get; set; }
}
