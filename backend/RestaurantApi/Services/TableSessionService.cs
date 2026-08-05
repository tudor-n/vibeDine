using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using RestaurantApi.Data;
using RestaurantApi.DTOs.Responses;
using RestaurantApi.Hubs;
using RestaurantApi.Models;

namespace RestaurantApi.Services;

/// <summary>How a session operation ended. The controller maps these to status codes.</summary>
public enum TableSessionOutcomeCode
{
    Ok,
    Created,
    NotFound,
    /// <summary>Someone else holds the table / the session is no longer open. → 409</summary>
    Conflict,
    /// <summary>Caller is neither the session owner nor a manager. → 403</summary>
    Forbidden,
    /// <summary>Bad payment method or amounts. → 400</summary>
    Invalid,
}

/// <summary>
/// Result of a session operation. On a claim conflict <see cref="Session"/> carries the
/// *existing* session so the console can name the colleague already serving the table.
/// </summary>
public sealed record TableSessionOutcome(
    TableSessionOutcomeCode Code,
    WaiterSessionDto? Session = null,
    string? Message = null);

public interface ITableSessionService
{
    /// <summary>
    /// Takes the table. Implemented as a plain INSERT: the partial unique index
    /// (table_id) WHERE status = 'open' is the arbiter, so two concurrent claims
    /// always resolve to exactly one winner.
    /// </summary>
    Task<TableSessionOutcome> ClaimTableAsync(Guid restaurantId, Guid tableId, Guid userId);

    Task<TableSessionOutcome> TransferAsync(Guid restaurantId, Guid sessionId, Guid toUserId, Guid actingUserId, bool canManage);

    Task<TableSessionOutcome> CloseAsync(
        Guid restaurantId, Guid sessionId, Guid actingUserId, bool canManage,
        string? paymentMethod, decimal? cashAmount, decimal? cardAmount);

    /// <summary>Manager escape hatch: closes the session without a payment (audit-trailed, no fiscal effect).</summary>
    Task<TableSessionOutcome> ForceReleaseAsync(Guid restaurantId, Guid sessionId, Guid actingUserId);

    /// <summary>The live bill: see <see cref="TableSessionService.BillableStatuses"/>.</summary>
    Task<decimal> GetCumulativeTotalAsync(Guid sessionId);

    Task<WaiterSessionDto?> GetSessionDtoAsync(Guid sessionId);

    /// <summary>The open session of each of the given tables, keyed by table id.</summary>
    Task<Dictionary<Guid, WaiterSessionDto>> GetOpenSessionsByTableAsync(IReadOnlyCollection<Guid> tableIds);

    Task<IReadOnlyList<WaiterSessionDto>> GetOpenSessionsForUserAsync(Guid userId);

    /// <summary>Transfer targets: active Waiter/RestaurantAdmin members of the restaurant.</summary>
    Task<IReadOnlyList<WaiterStaffMemberDto>> GetTransferTargetsAsync(Guid restaurantId);
}

public class TableSessionService : ITableSessionService
{
    /// <summary>
    /// What counts toward the bill (contract B.3-5). Deliberately includes <c>completed</c>
    /// and does NOT filter <c>DeletedAt</c>: a round archived by OrderFinalizationService is
    /// soft-deleted, and dropping it would make the total fall while the guests are still
    /// sitting there. Mirrors the device-session cumulative-total precedent.
    /// <c>draft</c> (not ordered yet), <c>cancelled</c> and <c>merged</c> stay out.
    /// </summary>
    public static readonly string[] BillableStatuses =
        ["pending_dispatch", "submitted", "preparing", "ready", "completed"];

    /// <summary>Orders still in flight on the board — what "2 comenzi active" on a floor tile counts.</summary>
    public static readonly string[] ActiveOrderStatuses =
        ["pending_dispatch", "submitted", "preparing", "ready"];

    private readonly AppDbContext _db;
    private readonly IOrderEventWriter _events;
    private readonly IHubContext<StaffHub> _staffHub;
    private readonly ILogger<TableSessionService> _logger;

    public TableSessionService(
        AppDbContext db,
        IOrderEventWriter events,
        IHubContext<StaffHub> staffHub,
        ILogger<TableSessionService> logger)
    {
        _db = db;
        _events = events;
        _staffHub = staffHub;
        _logger = logger;
    }

    // ── Claim ────────────────────────────────────────────────────────────────

    public async Task<TableSessionOutcome> ClaimTableAsync(Guid restaurantId, Guid tableId, Guid userId)
    {
        // Tenant filter + soft-delete: a waiter cannot claim another restaurant's table
        // (or a retired one) by pasting its id.
        var table = await _db.Tables
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == tableId && t.DeletedAt == null);
        if (table is null)
            return new TableSessionOutcome(TableSessionOutcomeCode.NotFound, Message: "Masa nu a fost găsită.");

        var waiterName = await _db.Users
            .Where(u => u.Id == userId)
            .Select(u => u.FullName)
            .FirstOrDefaultAsync() ?? "Ospătar";

        var session = new TableServiceSession
        {
            TableId = tableId,
            WaiterUserId = userId,
            WaiterName = waiterName,
            Status = "open",
            OpenedAt = DateTime.UtcNow,
            // RestaurantId deliberately left unset — StampTenant fills it from the
            // ambient tenant, so a body can never steer which restaurant this lands in.
        };
        _db.TableServiceSessions.Add(session);

        _events.Enqueue(_db, OrderEventTypes.SessionOpened, restaurantId, session.Id, new
        {
            sessionId = session.Id,
            tableId,
            tableNumber = table.TableNumber,
            waiterUserId = userId,
            waiterName,
            openedAt = session.OpenedAt,
        });

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // The index rejected us: another waiter's INSERT landed first. Drop our
            // failed inserts from the tracker so the context stays usable, then report
            // who actually holds the table.
            _db.Entry(session).State = EntityState.Detached;
            foreach (var entry in _db.ChangeTracker.Entries<OrderEvent>().Where(e => e.State == EntityState.Added).ToList())
                entry.State = EntityState.Detached;

            var existing = await LoadOpenSessionAsync(tableId);
            if (existing is null)
            {
                // Vanishingly rare: the winner closed between our failure and this read.
                return new TableSessionOutcome(TableSessionOutcomeCode.Conflict,
                    Message: "Masa a fost preluată de altcineva. Reîncearcă.");
            }

            return new TableSessionOutcome(
                TableSessionOutcomeCode.Conflict,
                await ToDtoAsync(existing),
                $"Masa este deja preluată de {existing.WaiterName}.");
        }

        // Adopt whatever was already running at this table: QR rounds submitted before
        // anyone claimed it belong on this bill, otherwise they'd never be billed by the
        // session. Only unattached, live orders — never steal from another session.
        var adopted = await _db.Orders
            .Where(o => o.TableId == tableId
                        && o.DeletedAt == null
                        && o.TableServiceSessionId == null
                        && ActiveOrderStatuses.Contains(o.Status))
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.TableServiceSessionId, session.Id));

        if (adopted > 0)
            _logger.LogInformation("Session {SessionId} adopted {Count} orphan order(s) at table {TableId}", session.Id, adopted, tableId);

        var dto = await ToDtoAsync(session, table.TableNumber);

        await BroadcastToStaffAsync(restaurantId, "TableSessionOpened", new
        {
            sessionId = session.Id,
            tableId,
            tableNumber = table.TableNumber,
            waiterUserId = userId,
            waiterName,
            openedAt = session.OpenedAt,
        });

        return new TableSessionOutcome(TableSessionOutcomeCode.Created, dto);
    }

    // ── Transfer ─────────────────────────────────────────────────────────────

    public async Task<TableSessionOutcome> TransferAsync(
        Guid restaurantId, Guid sessionId, Guid toUserId, Guid actingUserId, bool canManage)
    {
        var session = await _db.TableServiceSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == sessionId);
        if (session is null)
            return new TableSessionOutcome(TableSessionOutcomeCode.NotFound, Message: "Sesiunea nu a fost găsită.");

        if (session.Status != "open")
            return new TableSessionOutcome(TableSessionOutcomeCode.Conflict, Message: "Sesiunea este deja închisă.");

        if (!canManage && session.WaiterUserId != actingUserId)
            return new TableSessionOutcome(TableSessionOutcomeCode.Forbidden,
                Message: "Poți transfera doar mesele preluate de tine.");

        // The target must actually work here — otherwise a session could be parked on
        // an outsider's id and become unreachable from every console.
        var target = await FindTransferTargetAsync(restaurantId, toUserId);
        if (target is null)
            return new TableSessionOutcome(TableSessionOutcomeCode.Invalid,
                Message: "Colegul selectat nu este activ în acest restaurant.");

        var fromUserId = session.WaiterUserId;

        await using var tx = await _db.Database.BeginTransactionAsync();

        // Guarded update: if the session was closed between the read above and here,
        // this matches 0 rows and we report the conflict instead of resurrecting it.
        var rows = await _db.TableServiceSessions
            .Where(s => s.Id == sessionId && s.Status == "open")
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.WaiterUserId, toUserId)
                .SetProperty(x => x.WaiterName, target.FullName));

        if (rows == 0)
        {
            await tx.RollbackAsync();
            return new TableSessionOutcome(TableSessionOutcomeCode.Conflict, Message: "Sesiunea este deja închisă.");
        }

        _events.Enqueue(_db, OrderEventTypes.SessionTransferred, restaurantId, sessionId, new
        {
            sessionId,
            tableId = session.TableId,
            fromUserId,
            toUserId,
            toName = target.FullName,
            occurredAt = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync();
        await tx.CommitAsync();

        await BroadcastToStaffAsync(restaurantId, "TableSessionTransferred", new
        {
            sessionId,
            tableId = session.TableId,
            fromUserId,
            toUserId,
            toName = target.FullName,
        });

        return new TableSessionOutcome(TableSessionOutcomeCode.Ok, await GetSessionDtoAsync(sessionId));
    }

    // ── Close & force-release ────────────────────────────────────────────────

    public async Task<TableSessionOutcome> CloseAsync(
        Guid restaurantId, Guid sessionId, Guid actingUserId, bool canManage,
        string? paymentMethod, decimal? cashAmount, decimal? cardAmount)
    {
        var session = await _db.TableServiceSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == sessionId);
        if (session is null)
            return new TableSessionOutcome(TableSessionOutcomeCode.NotFound, Message: "Sesiunea nu a fost găsită.");

        if (session.Status != "open")
            return new TableSessionOutcome(TableSessionOutcomeCode.Conflict, Message: "Sesiunea este deja închisă.");

        if (!canManage && session.WaiterUserId != actingUserId)
            return new TableSessionOutcome(TableSessionOutcomeCode.Forbidden,
                Message: "Poți închide doar mesele preluate de tine.");

        var method = paymentMethod?.Trim().ToLowerInvariant();
        if (method is not ("cash" or "card" or "mixed"))
            return new TableSessionOutcome(TableSessionOutcomeCode.Invalid,
                Message: "Metodă de plată invalidă. Alege numerar, card sau mixt.");

        // An order still in pending_dispatch is mid-flight to the kitchen (the flash-deal
        // hold hasn't elapsed). Closing now would cut a receipt for a bill that is about
        // to grow, so block rather than guess.
        var inFlight = await _db.Orders
            .AnyAsync(o => o.TableServiceSessionId == sessionId
                           && o.DeletedAt == null
                           && o.Status == "pending_dispatch");
        if (inFlight)
            return new TableSessionOutcome(TableSessionOutcomeCode.Conflict,
                Message: "Există comenzi în curs de trimitere către bucătărie. Încearcă din nou în câteva momente.");

        var cumulativeTotal = await GetCumulativeTotalAsync(sessionId);

        if (method == "mixed")
        {
            if (cashAmount is null || cardAmount is null)
                return new TableSessionOutcome(TableSessionOutcomeCode.Invalid,
                    Message: "Pentru plata mixtă trebuie completate ambele sume (numerar și card).");

            if (decimal.Round(cashAmount.Value + cardAmount.Value, 2) != decimal.Round(cumulativeTotal, 2))
                return new TableSessionOutcome(TableSessionOutcomeCode.Invalid,
                    Message: $"Suma numerar + card trebuie să fie egală cu totalul notei ({cumulativeTotal:0.00}).");
        }

        return await FinalizeCloseAsync(
            restaurantId, session, actingUserId,
            closedReason: "paid",
            paymentMethod: method,
            cashAmount: method == "mixed" ? cashAmount : null,
            cardAmount: method == "mixed" ? cardAmount : null,
            cumulativeTotal: cumulativeTotal);
    }

    public async Task<TableSessionOutcome> ForceReleaseAsync(Guid restaurantId, Guid sessionId, Guid actingUserId)
    {
        var session = await _db.TableServiceSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == sessionId);
        if (session is null)
            return new TableSessionOutcome(TableSessionOutcomeCode.NotFound, Message: "Sesiunea nu a fost găsită.");

        if (session.Status != "open")
            return new TableSessionOutcome(TableSessionOutcomeCode.Conflict, Message: "Sesiunea este deja închisă.");

        // No payment method and no in-flight guard: this is the manager's way out of a
        // stuck table (waiter went home holding it). Nothing was collected, so Logistic
        // ignores this close fiscally — but closed_by_* records who did it.
        return await FinalizeCloseAsync(
            restaurantId, session, actingUserId,
            closedReason: "force_released",
            paymentMethod: null,
            cashAmount: null,
            cardAmount: null,
            cumulativeTotal: await GetCumulativeTotalAsync(sessionId));
    }

    private async Task<TableSessionOutcome> FinalizeCloseAsync(
        Guid restaurantId,
        TableServiceSession session,
        Guid actingUserId,
        string closedReason,
        string? paymentMethod,
        decimal? cashAmount,
        decimal? cardAmount,
        decimal cumulativeTotal)
    {
        var closedByName = await _db.Users
            .Where(u => u.Id == actingUserId)
            .Select(u => u.FullName)
            .FirstOrDefaultAsync();

        var closedAt = DateTime.UtcNow;

        await using var tx = await _db.Database.BeginTransactionAsync();

        // A half-built draft is not part of the bill — detach it instead of blocking the
        // close on it, so the waiter's abandoned cart can't hold the table hostage.
        await _db.Orders
            .Where(o => o.TableServiceSessionId == session.Id && o.Status == "draft")
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.TableServiceSessionId, (Guid?)null));

        // Everything on the receipt, matching the cumulativeTotal above.
        var orderIds = await _db.Orders
            .Where(o => o.TableServiceSessionId == session.Id && BillableStatuses.Contains(o.Status))
            .Select(o => o.Id)
            .ToListAsync();

        // The atomic guard: only one caller can flip an open session to closed. A second
        // close (double-tap, two devices) matches 0 rows and gets the conflict.
        var rows = await _db.TableServiceSessions
            .Where(s => s.Id == session.Id && s.Status == "open")
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Status, "closed")
                .SetProperty(x => x.ClosedAt, closedAt)
                .SetProperty(x => x.ClosedReason, closedReason)
                .SetProperty(x => x.PaymentMethod, paymentMethod)
                .SetProperty(x => x.CashAmount, cashAmount)
                .SetProperty(x => x.CardAmount, cardAmount)
                .SetProperty(x => x.TotalAmount, cumulativeTotal)
                .SetProperty(x => x.ClosedByUserId, (Guid?)actingUserId)
                .SetProperty(x => x.ClosedByName, closedByName));

        if (rows == 0)
        {
            await tx.RollbackAsync();
            return new TableSessionOutcome(TableSessionOutcomeCode.Conflict, Message: "Sesiunea este deja închisă.");
        }

        var tableNumber = await _db.Tables
            .Where(t => t.Id == session.TableId)
            .Select(t => t.TableNumber)
            .FirstOrDefaultAsync();

        // This is Logistic's fiscal trigger — it must commit with the close itself.
        _events.Enqueue(_db, OrderEventTypes.SessionClosed, restaurantId, session.Id, new
        {
            sessionId = session.Id,
            tableId = session.TableId,
            tableNumber,
            waiterUserId = session.WaiterUserId,
            waiterName = session.WaiterName,
            paymentMethod,
            cashAmount,
            cardAmount,
            totalAmount = cumulativeTotal,
            closedReason,
            orderIds,
            closedAt,
        });
        await _db.SaveChangesAsync();
        await tx.CommitAsync();

        await BroadcastToStaffAsync(restaurantId, "TableSessionClosed", new
        {
            sessionId = session.Id,
            tableId = session.TableId,
            tableNumber,
            paymentMethod,
            totalAmount = cumulativeTotal,
            closedReason,
        });

        return new TableSessionOutcome(TableSessionOutcomeCode.Ok, await GetSessionDtoAsync(session.Id));
    }

    // ── Reads ────────────────────────────────────────────────────────────────

    public async Task<decimal> GetCumulativeTotalAsync(Guid sessionId)
    {
        // Summed from the lines, not from Order.TotalAmount, so a cancelled or merged
        // round can never leak into the bill via a stale header total.
        return await (from item in _db.OrderItems
                      join order in _db.Orders on item.OrderId equals order.Id
                      where order.TableServiceSessionId == sessionId
                            && BillableStatuses.Contains(order.Status)
                      select (decimal?)(item.Quantity * item.UnitPriceAtPurchase))
                     .SumAsync() ?? 0m;
    }

    public async Task<WaiterSessionDto?> GetSessionDtoAsync(Guid sessionId)
    {
        var session = await _db.TableServiceSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == sessionId);
        return session is null ? null : await ToDtoAsync(session);
    }

    public async Task<Dictionary<Guid, WaiterSessionDto>> GetOpenSessionsByTableAsync(IReadOnlyCollection<Guid> tableIds)
    {
        if (tableIds.Count == 0) return [];

        var sessions = await _db.TableServiceSessions
            .AsNoTracking()
            .Where(s => s.Status == "open" && tableIds.Contains(s.TableId))
            .ToListAsync();

        return await BuildDtosAsync(sessions, keyed: s => s.TableId);
    }

    public async Task<IReadOnlyList<WaiterSessionDto>> GetOpenSessionsForUserAsync(Guid userId)
    {
        var sessions = await _db.TableServiceSessions
            .AsNoTracking()
            .Where(s => s.Status == "open" && s.WaiterUserId == userId)
            .OrderBy(s => s.OpenedAt)
            .ToListAsync();

        var byId = await BuildDtosAsync(sessions, keyed: s => s.Id);
        return sessions.Select(s => byId[s.Id]).ToList();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private Task<TableServiceSession?> LoadOpenSessionAsync(Guid tableId) =>
        _db.TableServiceSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.TableId == tableId && s.Status == "open");

    /// <summary>
    /// Resolves a transfer target from exactly the population <see cref="GetTransferTargetsAsync"/>
    /// offers — the two must agree, or the picker would list people the transfer rejects.
    /// Memberships are not tenant-filtered entities, so restaurantId is matched explicitly.
    /// </summary>
    private Task<AppUser?> FindTransferTargetAsync(Guid restaurantId, Guid toUserId) =>
        (from m in _db.Memberships
         join u in _db.Users on m.UserId equals u.Id
         where m.UserId == toUserId
               && m.RestaurantId == restaurantId
               && m.Status == MembershipStatus.Active
               && (m.Role == MembershipRole.Waiter || m.Role == MembershipRole.RestaurantAdmin)
         select u).FirstOrDefaultAsync();

    public async Task<IReadOnlyList<WaiterStaffMemberDto>> GetTransferTargetsAsync(Guid restaurantId) =>
        await (from m in _db.Memberships
               join u in _db.Users on m.UserId equals u.Id
               where m.RestaurantId == restaurantId
                     && m.Status == MembershipStatus.Active
                     && (m.Role == MembershipRole.Waiter || m.Role == MembershipRole.RestaurantAdmin)
               orderby u.FullName
               select new WaiterStaffMemberDto(u.Id, u.FullName, m.Role.ToString()))
              .ToListAsync();

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    private async Task<WaiterSessionDto> ToDtoAsync(TableServiceSession session, string? tableNumber = null)
    {
        var map = await BuildDtosAsync([session], keyed: s => s.Id, tableNumberOverride: tableNumber);
        return map[session.Id];
    }

    /// <summary>
    /// Projects sessions to DTOs with their table number, live bill and active-order count
    /// in three set-based queries, never one per session (the floor plan renders every table).
    /// </summary>
    private async Task<Dictionary<Guid, WaiterSessionDto>> BuildDtosAsync(
        IReadOnlyList<TableServiceSession> sessions,
        Func<TableServiceSession, Guid> keyed,
        string? tableNumberOverride = null)
    {
        var result = new Dictionary<Guid, WaiterSessionDto>();
        if (sessions.Count == 0) return result;

        var sessionIds = sessions.Select(s => s.Id).ToList();
        var tableIds = sessions.Select(s => s.TableId).Distinct().ToList();

        var tableNumbers = tableNumberOverride is not null && sessions.Count == 1
            ? new Dictionary<Guid, string?> { [sessions[0].TableId] = tableNumberOverride }
            : await _db.Tables
                .Where(t => tableIds.Contains(t.Id))
                .Select(t => new { t.Id, t.TableNumber })
                .ToDictionaryAsync(t => t.Id, t => t.TableNumber);

        var totals = (await (from item in _db.OrderItems
                             join order in _db.Orders on item.OrderId equals order.Id
                             where order.TableServiceSessionId != null
                                   && sessionIds.Contains(order.TableServiceSessionId.Value)
                                   && BillableStatuses.Contains(order.Status)
                             group item by order.TableServiceSessionId!.Value into g
                             select new { SessionId = g.Key, Total = g.Sum(i => i.Quantity * i.UnitPriceAtPurchase) })
                            .ToListAsync())
            .ToDictionary(x => x.SessionId, x => x.Total);

        var activeCounts = (await _db.Orders
                .Where(o => o.TableServiceSessionId != null
                            && sessionIds.Contains(o.TableServiceSessionId.Value)
                            && o.DeletedAt == null
                            && ActiveOrderStatuses.Contains(o.Status))
                .GroupBy(o => o.TableServiceSessionId!.Value)
                .Select(g => new { SessionId = g.Key, Count = g.Count() })
                .ToListAsync())
            .ToDictionary(x => x.SessionId, x => x.Count);

        foreach (var s in sessions)
        {
            result[keyed(s)] = new WaiterSessionDto(
                s.Id,
                s.TableId,
                tableNumbers.GetValueOrDefault(s.TableId),
                s.WaiterUserId,
                s.WaiterName,
                s.Status,
                s.OpenedAt,
                s.ClosedAt,
                s.ClosedReason,
                s.PaymentMethod,
                s.CashAmount,
                s.CardAmount,
                s.TotalAmount,
                activeCounts.GetValueOrDefault(s.Id),
                totals.GetValueOrDefault(s.Id));
        }

        return result;
    }

    /// <summary>
    /// Every staff console (admin tabs and waiter phones) needs floor state, so session
    /// events go to both role groups — group names always via <see cref="TenantGroups"/>,
    /// payloads always anonymous DTOs, never EF entities.
    /// </summary>
    private async Task BroadcastToStaffAsync(Guid restaurantId, string eventName, object payload)
    {
        await _staffHub.Clients.Group(TenantGroups.Role(restaurantId, "admin")).SendAsync(eventName, payload);
        await _staffHub.Clients.Group(TenantGroups.Role(restaurantId, "WaitStaff")).SendAsync(eventName, payload);
    }
}
