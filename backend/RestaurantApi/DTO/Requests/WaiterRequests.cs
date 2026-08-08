using System.ComponentModel.DataAnnotations;

namespace RestaurantApi.DTOs.Requests;

public class WaiterSignupRequest
{
    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required, MinLength(8)]
    public string Password { get; set; } = string.Empty;

    [Required]
    public string FullName { get; set; } = string.Empty;
}

// ── Waiter mode (table service sessions) ─────────────────────────────────────
// None of these carry a restaurantId / session id / order id: those always come
// from the route and are verified against the tenant filter, never bound from a
// body an authenticated waiter controls.

public record TransferSessionRequest(Guid ToUserId);

/// <summary>
/// Payment is physical at the table — this only records how it was taken.
/// <c>"cash"</c> | <c>"card"</c> | <c>"mixed"</c>; the two amounts are required
/// (and must add up to the bill) only for <c>"mixed"</c>.
/// </summary>
public record CloseSessionRequest(string? PaymentMethod, decimal? CashAmount, decimal? CardAmount);

public record WaiterOrderItemRequest(Guid ProductId, int Quantity, string? Notes);

/// <summary>Full-replace cart semantics, like the customer PATCH: omission removes the line.</summary>
public record UpdateWaiterOrderItemsRequest(List<WaiterOrderItemRequest>? Items);

// ── Masa auxiliară ───────────────────────────────────────────────────────────

/// <summary>Null quantity moves the whole line.</summary>
public record MoveItemToAuxRequest(int? Quantity);

/// <summary>Target of an aux → table move; the table must carry the caller's open session.</summary>
public record MoveAuxItemToTableRequest(Guid TableId);
