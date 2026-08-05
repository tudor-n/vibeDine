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

// ── Waiter mode (table service sessions) ─────────────────────────────────────
// These mirror shared/types/waiter.ts field-for-field. The TS side is hand-synced
// (W3 owns it), so a rename here silently breaks the waiter console — change both.
// Positional records: System.Text.Json serialises them camelCase via the global policy.

/// <summary>A waiter's claim on a table. <c>CumulativeTotal</c> is the live bill; <c>TotalAmount</c> is its snapshot at close.</summary>
public record WaiterSessionDto(
    Guid Id,
    Guid TableId,
    string? TableNumber,
    Guid? WaiterUserId,
    string WaiterName,
    string Status,
    DateTime OpenedAt,
    DateTime? ClosedAt,
    string? ClosedReason,
    string? PaymentMethod,
    decimal? CashAmount,
    decimal? CardAmount,
    decimal? TotalAmount,
    int ActiveOrderCount,
    decimal CumulativeTotal);

public record WaiterOrderItemDto(
    Guid Id,
    Guid ProductId,
    string ProductName,
    int Quantity,
    decimal UnitPriceAtPurchase,
    string? Notes,
    bool IsFlashDealItem);

public record WaiterOrderDto(
    Guid Id,
    int? OrderNumber,
    string Status,
    string Source,
    Guid? PlacedByUserId,
    decimal TotalAmount,
    DateTime CreatedAt,
    IReadOnlyList<WaiterOrderItemDto> Items);

public record WaiterSessionDetailDto(
    WaiterSessionDto Session,
    IReadOnlyList<WaiterOrderDto> Orders,
    decimal CumulativeTotal);

/// <summary><c>Session</c> null means a free table.</summary>
public record WaiterFloorTableDto(
    Guid Id,
    string? TableNumber,
    Guid? ZoneId,
    int Capacity,
    string Shape,
    double PositionX,
    double PositionY,
    WaiterSessionDto? Session,
    bool HasOpenHelpRequest,
    int ActiveOrderCount);

public record WaiterFloorZoneDto(Guid Id, string Name, int SortOrder);

public record WaiterFloorResponse(
    IReadOnlyList<WaiterFloorZoneDto> Zones,
    IReadOnlyList<WaiterFloorTableDto> Tables);

/// <summary>A transfer target: an active Waiter or RestaurantAdmin of this restaurant.</summary>
public record WaiterStaffMemberDto(Guid UserId, string FullName, string Role);

/// <summary>Terse submit/cancel acknowledgements — the console refetches the session for the rest.</summary>
public record WaiterSubmitOrderResponse(Guid OrderId, int? OrderNumber, string Status, decimal TotalAmount);

public record WaiterCancelOrderResponse(Guid Id, string Status);

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
