namespace PlatformApi.DTO.Responses;

public record PlatformAdminDto(Guid Id, string Email, string FullName);

public record AuthResponse(string AccessToken, DateTime ExpiresAt, PlatformAdminDto Admin);

public record MetricsResponse(
    int Organizations,
    int Locations,
    int ActiveLocations,
    int InactiveLocations,
    int Admins,
    int Hires,
    int PendingHires);

public record UserSummary(Guid Id, string FullName, string? Email);

public record OrganizationListItem(
    Guid Id,
    string Name,
    string Slug,
    DateTime CreatedAt,
    int LocationCount,
    int AdminCount,
    int HireCount,
    UserSummary? Owner);

public record MemberDto(
    Guid MembershipId,
    Guid UserId,
    string FullName,
    string? Email,
    string Role,
    string Status,
    Guid? RestaurantId,
    DateTime CreatedAt);

public record LocationListItem(
    Guid Id,
    Guid OrganizationId,
    string OrganizationName,
    string Name,
    string Slug,
    string? Address,
    bool IsActive,
    string Timezone,
    string Currency,
    string Locale,
    DateTime CreatedAt,
    int AdminCount,
    int HireCount);

public record LocationDetail(
    Guid Id,
    Guid OrganizationId,
    string OrganizationName,
    string Name,
    string Slug,
    string? Address,
    bool IsActive,
    string Timezone,
    string Currency,
    string Locale,
    DateTime CreatedAt,
    IReadOnlyList<MemberDto> Admins,
    IReadOnlyList<MemberDto> Hires);

public record OrganizationDetail(
    Guid Id,
    string Name,
    string Slug,
    DateTime CreatedAt,
    IReadOnlyList<UserSummary> Owners,
    IReadOnlyList<LocationListItem> Locations);

public record RegistrationLinkDto(
    Guid Id,
    Guid Token,
    string Type,
    Guid? OrganizationId,
    string? OrganizationName,
    string? Label,
    string? IntendedEmail,
    string Status,
    string Url,
    DateTime ExpiresAt,
    DateTime CreatedAt,
    DateTime? UsedAt);
