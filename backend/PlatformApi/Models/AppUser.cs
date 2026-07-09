namespace PlatformApi.Models;

/// <summary>
/// Read-only projection of the main app's <c>AspNetUsers</c> table. Only the columns the
/// platform console needs are mapped; this service never creates or modifies users.
/// </summary>
public class AppUser
{
    public Guid Id { get; set; }
    public string? Email { get; set; }
    public string FullName { get; set; } = string.Empty;
    public bool EmailConfirmed { get; set; }
    public DateTime CreatedAt { get; set; }

    public ICollection<Membership> Memberships { get; set; } = [];
}
