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
        var table = await _db.Tables.FirstOrDefaultAsync(t => t.QrToken == qrToken);
        if (table is null)
            return NotFound(new { message = "Invalid QR Code. Table not found." });

        // Resolve the guest only if the passed id actually belongs to THIS table —
        // a stored id from another table (or a forged one) starts a fresh slot here.
        TableGuest? guest = null;
        if (body?.GuestId is Guid gid && gid != Guid.Empty)
            guest = await _db.TableGuests.FirstOrDefaultAsync(g => g.Id == gid && g.TableId == table.Id);

        var isNewGuest = guest is null;
        if (guest is null)
        {
            var seated = await _db.TableGuests.CountAsync(g => g.TableId == table.Id);
            guest = new TableGuest
            {
                TableId = table.Id,
                RestaurantId = table.RestaurantId,
                DisplayName = $"Guest {seated + 1}",
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
            activeOrder = MapOrder(draft),
            partyMembers = await BuildPartyAsync(table.Id),
        });
    }

    // GET /api/tables/{qrToken}/party — every phone polls/refetches this to render
    // the party panel (who's at the table + each cart's running total).
    [HttpGet("tables/{qrToken:guid}/party")]
    public async Task<IActionResult> Party(Guid qrToken)
    {
        var table = await _db.Tables.FirstOrDefaultAsync(t => t.QrToken == qrToken);
        if (table is null)
            return NotFound(new { message = "Table not found." });

        return Ok(new { partyMembers = await BuildPartyAsync(table.Id) });
    }

    // POST /api/tables/{qrToken}/merge — consolidate every other draft at this table
    // into the initiator's draft so the table pays one combined bill.
    [HttpPost("tables/{qrToken:guid}/merge")]
    public async Task<IActionResult> Merge(Guid qrToken, [FromBody] MergePartyRequest body)
    {
        var table = await _db.Tables.FirstOrDefaultAsync(t => t.QrToken == qrToken);
        if (table is null)
            return NotFound(new { message = "Table not found." });

        if (body is null || body.InitiatorGuestId == Guid.Empty)
            return BadRequest(new { message = "Missing initiator guest." });

        // The initiator must actually be seated at this table.
        var initiator = await _db.TableGuests
            .FirstOrDefaultAsync(g => g.Id == body.InitiatorGuestId && g.TableId == table.Id);
        if (initiator is null)
            return BadRequest(new { message = "Guest is not seated at this table." });

        await using var tx = await _db.Database.BeginTransactionAsync();

        var target = await _db.Orders
            .FirstOrDefaultAsync(o => o.TableId == table.Id && o.GuestId == initiator.Id && o.Status == "draft");
        if (target is null)
            return Conflict(new { message = "Your order is no longer an open draft." });

        // Every OTHER still-open draft at this table (submitted orders are left
        // alone — those guests already chose to pay separately).
        var sources = await _db.Orders
            .Where(o => o.TableId == table.Id && o.Status == "draft" && o.Id != target.Id)
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

    // Each guest's current order summary for the party panel.
    private async Task<List<object>> BuildPartyAsync(Guid tableId)
    {
        var guests = await _db.TableGuests
            .Where(g => g.TableId == tableId)
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
