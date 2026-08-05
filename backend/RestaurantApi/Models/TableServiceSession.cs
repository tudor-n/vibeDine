using System.ComponentModel.DataAnnotations.Schema;

namespace RestaurantApi.Models;

/// <summary>
/// A waiter's exclusive service claim on a table: opened when a waiter takes the table
/// ("Masa mea"), closed when the bill is paid (or force-released by a manager). Every
/// order placed at the table while the session is open — waiter-entered *and* QR — is
/// attached to it, so the session is the unit the bill and the fiscal receipt are cut from.
///
/// Exclusivity is enforced by the database, not by application logic: a partial unique
/// index on (table_id) WHERE status = 'open' makes a second concurrent claim fail with a
/// 23505 unique violation, which <see cref="Services.TableSessionService"/> turns into a
/// conflict result. Claim is therefore a plain INSERT — no read-then-write race window.
/// </summary>
public class TableServiceSession : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid RestaurantId { get; set; }

    public Guid TableId { get; set; }
    public RestaurantTable? Table { get; set; }

    /// <summary>The waiter currently serving the table. Null only if the user was deleted (SetNull).</summary>
    public Guid? WaiterUserId { get; set; }
    public AppUser? WaiterUser { get; set; }

    /// <summary>Snapshot of the owner's FullName at claim/transfer time, so the bill survives user deletion.</summary>
    public string WaiterName { get; set; } = string.Empty;

    /// <summary>"open" | "closed" — exact string literals, no enum (matches the Order.Status convention).</summary>
    public string Status { get; set; } = "open";

    public DateTime OpenedAt { get; set; } = DateTime.UtcNow;

    public DateTime? ClosedAt { get; set; }

    /// <summary>"paid" | "force_released". Null while open.</summary>
    public string? ClosedReason { get; set; }

    /// <summary>"cash" | "card" | "mixed". Null while open and on a force-release (nothing was collected).</summary>
    public string? PaymentMethod { get; set; }

    [Column(TypeName = "decimal(10,2)")]
    public decimal? CashAmount { get; set; }

    [Column(TypeName = "decimal(10,2)")]
    public decimal? CardAmount { get; set; }

    /// <summary>Snapshot of the cumulative total at close. Null while open (the live sum is computed on read).</summary>
    [Column(TypeName = "decimal(10,2)")]
    public decimal? TotalAmount { get; set; }

    /// <summary>Audit trail: who closed or force-released the session (may differ from the owner).</summary>
    public Guid? ClosedByUserId { get; set; }

    public string? ClosedByName { get; set; }
}
