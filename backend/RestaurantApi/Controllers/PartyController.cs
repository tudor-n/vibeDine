using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using RestaurantApi.Data;
using RestaurantApi.DTOs.Requests;
using RestaurantApi.DTOs.Responses;
using RestaurantApi.Hubs;
using RestaurantApi.Models;

namespace RestaurantApi.Controllers;

/// <summary>
/// Multi-person ordering at one table. Each phone is a <see cref="TableGuest"/>
/// that owns its own draft order; at checkout the party can merge every draft
/// into one bill or submit separately. Routes are keyed on <c>qrToken</c> so the
/// <see cref="Tenancy.TenantResolutionMiddleware"/> resolves the tenant (the merge
/// route deliberately avoids a bare tableId, which carries no tenant context).
/// All endpoints are anonymous — the customer app has no login.
/// </summary>
[ApiController]
[Route("api")]
public class PartyController : ControllerBase
{
    // A guest idle past this with nothing in flight is presumed gone; their seat
    // (and leftover empty draft) is reclaimed on the next scan so yesterday's
    // party doesn't haunt today's panel.
    private static readonly TimeSpan StaleGuestAfter = TimeSpan.FromHours(3);

    // A guest whose cart is still EMPTY and who hasn't been heard from for this
    // long almost certainly scanned and walked away. Their seat is reclaimed much
    // sooner than StaleGuestAfter so abandoned scans don't hold "Guest N" slots
    // hostage all afternoon. Guests with items in their cart keep the full
    // 3-hour grace — never destroy a composed cart over a locked phone.
    private static readonly TimeSpan AbandonedEmptyAfter = TimeSpan.FromMinutes(10);

    // How fresh a guest's heartbeat must be to count as "at the table". The
    // customer app polls /party every ~15s and that poll carries its guestId
    // (the heartbeat); 75s tolerates browsers throttling hidden tabs to one
    // timer tick per minute without making present guests flicker. Phones that
    // stop beating (tab closed, customer left) drop out of the panel within
    // about a minute, even though their row waits for the lazy sweep.
    private static readonly TimeSpan PresenceWindow = TimeSpan.FromSeconds(75);

    private readonly AppDbContext _db;
    private readonly IHubContext<CustomerHub> _customerHub;

    public PartyController(AppDbContext db, IHubContext<CustomerHub> customerHub)
    {
        _db = db;
        _customerHub = customerHub;
    }

    // POST /api/tables/{qrToken}/join — claim/restore this phone's guest slot and
    // its draft. First-time scanners get a fresh "Guest N"; returning phones (that
    // pass their stored guestId) get their existing draft back.
    [HttpPost("tables/{qrToken:guid}/join")]
    public async Task<IActionResult> Join(Guid qrToken, [FromBody] JoinPartyRequest? body)
    {
        var table = await _db.Tables.FirstOrDefaultAsync(t => t.QrToken == qrToken && t.DeletedAt == null);
        if (table is null)
            return NotFound(new { message = "Invalid QR Code. Table not found." });

        await PurgeStaleGuestsAsync(table.Id);

        // Resolve the guest only if the passed id actually belongs to THIS table —
        // a stored id from another table (or a forged one) starts a fresh slot here.
        TableGuest? guest = null;
        if (body?.GuestId is Guid gid && gid != Guid.Empty)
            guest = await _db.TableGuests.FirstOrDefaultAsync(g => g.Id == gid && g.TableId == table.Id);

        var isNewGuest = guest is null;
        if (guest is null)
        {
            guest = new TableGuest
            {
                TableId = table.Id,
                RestaurantId = table.RestaurantId,
                DisplayName = $"Guest {await NextFreeGuestNumberAsync(table.Id)}",
            };
            _db.TableGuests.Add(guest);
        }
        else
        {
            guest.LastSeenAt = DateTime.UtcNow;
        }
        await _db.SaveChangesAsync();

        // Each guest owns exactly one live draft.
        var draft = await _db.Orders
            .Include(o => o.Items).ThenInclude(i => i.Product)
            .FirstOrDefaultAsync(o => o.TableId == table.Id && o.GuestId == guest.Id && o.Status == "draft");

        if (draft is null)
        {
            draft = new Order
            {
                TableId = table.Id,
                GuestId = guest.Id,
                RestaurantId = table.RestaurantId,
                Status = "draft",
                PaymentStatus = "unpaid",
                TotalAmount = 0,
            };
            _db.Orders.Add(draft);
            await _db.SaveChangesAsync();
        }

        if (isNewGuest)
        {
            await _customerHub.Clients
                .Group(TenantGroups.Table(table.RestaurantId, table.Id))
                .SendAsync("PartyMemberJoined", new { guestId = guest.Id, displayName = guest.DisplayName });
        }

        var restaurant = await _db.Restaurants
            .Include(r => r.Organization)
            .FirstOrDefaultAsync(r => r.Id == table.RestaurantId);

        return Ok(new
        {
            guestId = guest.Id,
            displayName = guest.DisplayName,
            restaurantId = table.RestaurantId,
            restaurantName = restaurant?.Name ?? "Restaurant",
            restaurantAddress = restaurant?.Address,
            organizationName = restaurant?.Organization?.Name ?? string.Empty,
            tableId = table.Id,
            tableNumber = table.TableNumber ?? "Unknown Table",
            branding = new
            {
                enabled = restaurant?.BrandingEnabled ?? false,
                primary = restaurant?.BrandPrimary,
                secondary = restaurant?.BrandSecondary,
                background = restaurant?.BrandBackground,
                logoUrl = restaurant?.LogoUrl,
                photoUrl = restaurant?.PhotoUrl,
            },
            activeOrder = MapOrder(draft),
            partyMembers = await BuildPartyAsync(table.Id),
        });
    }

    // GET /api/tables/{qrToken}/party — every phone polls/refetches this to render
    // the party panel (who's at the table + each cart's running total).
    [HttpGet("tables/{qrToken:guid}/party")]
    public async Task<IActionResult> Party(Guid qrToken, [FromQuery] Guid? guestId)
    {
        var table = await _db.Tables.FirstOrDefaultAsync(t => t.QrToken == qrToken && t.DeletedAt == null);
        if (table is null)
            return NotFound(new { message = "Table not found." });

        // The poll doubles as a presence heartbeat: the calling phone passes its
        // guestId so its seat stays inside the PresenceWindow while the tab lives.
        if (guestId is Guid gid && gid != Guid.Empty)
        {
            var me = await _db.TableGuests.FirstOrDefaultAsync(g => g.Id == gid && g.TableId == table.Id);
            if (me is not null)
            {
                me.LastSeenAt = DateTime.UtcNow;
                await _db.SaveChangesAsync();
            }
        }

        return Ok(new { partyMembers = await BuildPartyAsync(table.Id) });
    }

    // POST /api/tables/{qrToken}/merge — consolidate every other draft at this table
    // into the initiator's draft so the table pays one combined bill.
    [HttpPost("tables/{qrToken:guid}/merge")]
    public async Task<IActionResult> Merge(Guid qrToken, [FromBody] MergePartyRequest body)
    {
        var table = await _db.Tables.FirstOrDefaultAsync(t => t.QrToken == qrToken && t.DeletedAt == null);
        if (table is null)
            return NotFound(new { message = "Table not found." });

        if (body is null || body.InitiatorGuestId == Guid.Empty)
            return BadRequest(new { message = "Missing initiator guest." });

        // The initiator must actually be seated at this table.
        var initiator = await _db.TableGuests
            .FirstOrDefaultAsync(g => g.Id == body.InitiatorGuestId && g.TableId == table.Id);
        if (initiator is null)
            return BadRequest(new { message = "Guest is not seated at this table." });

        // Merging is activity — it proves this phone is still at the table.
        initiator.LastSeenAt = DateTime.UtcNow;

        await using var tx = await _db.Database.BeginTransactionAsync();

        var target = await _db.Orders
            .FirstOrDefaultAsync(o => o.TableId == table.Id && o.GuestId == initiator.Id && o.Status == "draft");
        if (target is null)
            return Conflict(new { message = "Your order is no longer an open draft." });

        // Every OTHER still-open draft at this table whose owner is still present
        // (submitted orders are left alone — those guests already chose to pay
        // separately). Drafts of absent guests stay out too: a stale seat the
        // sweep hasn't reclaimed yet must not inject surprise items into the
        // combined bill — and the panel the initiator just looked at didn't
        // show those guests either.
        var presentCutoff = DateTime.UtcNow - PresenceWindow;
        var sources = await _db.Orders
            .Where(o => o.TableId == table.Id && o.Status == "draft" && o.Id != target.Id
                && o.GuestId != null
                && _db.TableGuests.Any(g => g.Id == o.GuestId && g.LastSeenAt >= presentCutoff))
            .ToListAsync();

        var mergedIds = sources.Select(s => s.Id).ToList();

        if (mergedIds.Count > 0)
        {
            // Re-parent the items at the DB level (no navigation-fixup ambiguity).
            // Drafts never hold flash-deal items (deals are created post-submit),
            // so every item moves. RestaurantId is unchanged (same restaurant).
            await _db.OrderItems
                .Where(i => mergedIds.Contains(i.OrderId))
                .ExecuteUpdateAsync(s => s.SetProperty(i => i.OrderId, target.Id));

            foreach (var src in sources)
            {
                src.Status = "merged";
                src.MergedIntoOrderId = target.Id;
            }
            await _db.SaveChangesAsync();

            target.TotalAmount = await _db.OrderItems
                .Where(i => i.OrderId == target.Id)
                .SumAsync(i => i.Quantity * i.UnitPriceAtPurchase);
            await _db.SaveChangesAsync();
        }

        await tx.CommitAsync();

        await _customerHub.Clients
            .Group(TenantGroups.Table(table.RestaurantId, table.Id))
            .SendAsync("PartyMerged", new
            {
                targetOrderId = target.Id,
                mergedOrderIds = mergedIds,
                initiatorGuestId = initiator.Id,
                initiatorDisplayName = initiator.DisplayName ?? "A guest",
            });

        var full = await _db.Orders
            .Include(o => o.Items).ThenInclude(i => i.Product)
            .FirstAsync(o => o.Id == target.Id);

        return Ok(new
        {
            targetOrderId = target.Id,
            mergedOrderIds = mergedIds,
            activeOrder = MapOrder(full),
            partyMembers = await BuildPartyAsync(table.Id),
        });
    }

    // Guest numbers recycle: seats vanish when their owner submits (see
    // CustomerController.SubmitOrder) or gets swept below, so the next scanner
    // takes the lowest hole — a fresh table starts back at "Guest 1" instead of
    // the numbers climbing all day. Numbers come from the live rows' names (not
    // a count) so a freed middle seat is refilled without duplicating a name
    // still in use. Two phones joining in the same instant could both grab the
    // same number; like the OrderNumber sequence, that race is tolerated.
    private async Task<int> NextFreeGuestNumberAsync(Guid tableId)
    {
        var names = await _db.TableGuests
            .Where(g => g.TableId == tableId)
            .Select(g => g.DisplayName)
            .ToListAsync();

        var taken = new HashSet<int>();
        foreach (var name in names)
        {
            if (name is not null && name.StartsWith("Guest ")
                && int.TryParse(name.AsSpan("Guest ".Length), out var n))
                taken.Add(n);
        }

        var slot = 1;
        while (taken.Contains(slot)) slot++;
        return slot;
    }

    // Submit already frees a guest's seat the moment their order leaves for the
    // kitchen (CustomerController.SubmitOrder), so the only seats left to reap
    // here are phones that scanned but never sent anything: empty carts go after
    // AbandonedEmptyAfter, non-empty carts after StaleGuestAfter. An in-flight
    // order (pending_dispatch..ready) protects the seat regardless of idle time.
    private async Task PurgeStaleGuestsAsync(Guid tableId)
    {
        var now = DateTime.UtcNow;
        var staleCutoff = now - StaleGuestAfter;
        var abandonedCutoff = now - AbandonedEmptyAfter;
        var inFlight = new[] { "pending_dispatch", "submitted", "preparing", "ready" };

        var staleIds = await _db.TableGuests
            .Where(g => g.TableId == tableId)
            .Where(g => !_db.Orders.Any(o => o.GuestId == g.Id && inFlight.Contains(o.Status)))
            .Where(g => g.LastSeenAt < staleCutoff
                || (g.LastSeenAt < abandonedCutoff
                    && !_db.Orders.Any(o => o.GuestId == g.Id && o.Status == "draft" && o.Items.Any())))
            .Select(g => g.Id)
            .ToListAsync();
        if (staleIds.Count == 0) return;

        // Abandoned drafts (and their items) go with the seat; submitted/merged
        // orders stay — they're kitchen history, not party state.
        await _db.OrderItems
            .Where(i => _db.Orders.Any(o => o.Id == i.OrderId && o.Status == "draft"
                && o.GuestId != null && staleIds.Contains(o.GuestId.Value)))
            .ExecuteDeleteAsync();
        await _db.Orders
            .Where(o => o.Status == "draft" && o.GuestId != null && staleIds.Contains(o.GuestId.Value))
            .ExecuteDeleteAsync();
        await _db.TableGuests
            .Where(g => staleIds.Contains(g.Id))
            .ExecuteDeleteAsync();
    }

    // Each guest's current order summary for the party panel. Only guests heard
    // from within the PresenceWindow are listed: a row can outlive the person
    // (the sweep is lazy), but the panel must only show phones that are actually
    // still here and beating.
    private async Task<List<object>> BuildPartyAsync(Guid tableId)
    {
        var presentCutoff = DateTime.UtcNow - PresenceWindow;
        var guests = await _db.TableGuests
            .Where(g => g.TableId == tableId && g.LastSeenAt >= presentCutoff)
            .OrderBy(g => g.CreatedAt)
            .ToListAsync();

        var orders = await _db.Orders
            .Include(o => o.Items)
            .Where(o => o.TableId == tableId && o.GuestId != null)
            .ToListAsync();

        // Most recent order per guest (a guest has one draft until it submits/merges).
        var latestByGuest = orders
            .GroupBy(o => o.GuestId!.Value)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(o => o.CreatedAt).First());

        return guests.Select(g =>
        {
            latestByGuest.TryGetValue(g.Id, out var order);
            return (object)new
            {
                guestId = g.Id,
                displayName = g.DisplayName,
                itemCount = order?.Items.Sum(i => i.Quantity) ?? 0,
                // Sum live from the items rather than the stored TotalAmount: the
                // draft cart-sync (CustomerController.AddItemsOrder) leaves a stale
                // doubled TotalAmount on drafts, and submit/merge recompute it anyway.
                total = order?.Items.Sum(i => i.Quantity * i.UnitPriceAtPurchase) ?? 0m,
                status = order?.Status ?? "draft",
            };
        }).ToList();
    }

    private static OrderResponse MapOrder(Order o) => new()
    {
        Id = o.Id,
        Status = o.Status ?? "draft",
        TotalAmount = o.TotalAmount,
        Items = (o.Items ?? new List<OrderItem>())
            .Select(i => new OrderItemResponse
            {
                ProductId = i.ProductId,
                ProductName = i.Product?.Name ?? "Unknown Item",
                Quantity = i.Quantity,
                UnitPrice = i.UnitPriceAtPurchase,
                IsFlashDealItem = i.IsFlashDealItem ?? false,
                ImageUrl = i.Product?.ImageUrl,
            })
            .ToList(),
    };
}
