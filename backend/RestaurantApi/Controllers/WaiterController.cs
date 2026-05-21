using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RestaurantApi.Auth;
using RestaurantApi.Data;
using RestaurantApi.DTOs.Requests;
using RestaurantApi.DTOs.Responses;
using RestaurantApi.Models;
using RestaurantApi.Services.Email;
using RestaurantApi.Tenancy;

namespace RestaurantApi.Controllers;

/// <summary>
/// Waiter onboarding & approval. Managers (OrgOwner or RestaurantAdmin) generate a reusable
/// per-restaurant signup link, share it with prospective waiters, then approve or reject the
/// resulting pending memberships from the dashboard.
/// </summary>
[ApiController]
public class WaiterController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ITenantAccess _access;
    private readonly UserManager<AppUser> _users;
    private readonly IEmailSender _email;
    private readonly AppUrlOptions _appUrls;
    private readonly ILogger<WaiterController> _logger;

    public WaiterController(
        AppDbContext db,
        ITenantAccess access,
        UserManager<AppUser> users,
        IEmailSender email,
        IOptions<AppUrlOptions> appUrls,
        ILogger<WaiterController> logger)
    {
        _db = db;
        _access = access;
        _users = users;
        _email = email;
        _appUrls = appUrls.Value;
        _logger = logger;
    }

    private Guid UserId =>
        Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub")!);

    // ── Manager: signup-link CRUD ────────────────────────────────────────────

    [Authorize]
    [HttpGet("api/restaurants/{restaurantId:guid}/waiter-link")]
    public async Task<IActionResult> GetActiveLink(Guid restaurantId)
    {
        if (!await _access.CanManageRestaurantAsync(UserId, restaurantId)) return Forbid();

        var link = await _db.WaiterSignupLinks
            .Where(l => l.RestaurantId == restaurantId && l.IsActive)
            .OrderByDescending(l => l.CreatedAt)
            .FirstOrDefaultAsync();

        if (link == null)
        {
            // Lazy-create the first link the first time the manager opens the panel.
            var restaurant = await _db.Restaurants
                .Where(r => r.Id == restaurantId)
                .Select(r => new { r.OrganizationId })
                .FirstOrDefaultAsync();
            if (restaurant == null) return NotFound();

            link = new WaiterSignupLink
            {
                OrganizationId = restaurant.OrganizationId,
                RestaurantId = restaurantId,
                CreatedByUserId = UserId,
            };
            _db.WaiterSignupLinks.Add(link);
            await _db.SaveChangesAsync();
        }

        return Ok(ToResponse(link));
    }

    [Authorize]
    [HttpPost("api/restaurants/{restaurantId:guid}/waiter-link/regenerate")]
    public async Task<IActionResult> Regenerate(Guid restaurantId)
    {
        if (!await _access.CanManageRestaurantAsync(UserId, restaurantId)) return Forbid();

        var restaurant = await _db.Restaurants
            .Where(r => r.Id == restaurantId)
            .Select(r => new { r.OrganizationId })
            .FirstOrDefaultAsync();
        if (restaurant == null) return NotFound();

        var existing = await _db.WaiterSignupLinks
            .Where(l => l.RestaurantId == restaurantId && l.IsActive)
            .ToListAsync();
        foreach (var l in existing) l.IsActive = false;

        var fresh = new WaiterSignupLink
        {
            OrganizationId = restaurant.OrganizationId,
            RestaurantId = restaurantId,
            CreatedByUserId = UserId,
        };
        _db.WaiterSignupLinks.Add(fresh);
        await _db.SaveChangesAsync();
        _logger.LogInformation("Restaurant {RestaurantId}: regenerated waiter signup link", restaurantId);

        return Ok(ToResponse(fresh));
    }

    [Authorize]
    [HttpPost("api/restaurants/{restaurantId:guid}/waiter-link/revoke")]
    public async Task<IActionResult> Revoke(Guid restaurantId)
    {
        if (!await _access.CanManageRestaurantAsync(UserId, restaurantId)) return Forbid();

        var existing = await _db.WaiterSignupLinks
            .Where(l => l.RestaurantId == restaurantId && l.IsActive)
            .ToListAsync();
        foreach (var l in existing) l.IsActive = false;
        await _db.SaveChangesAsync();

        return NoContent();
    }

    // ── Public: waiter signup landing ────────────────────────────────────────

    [AllowAnonymous]
    [HttpGet("api/waiter-signup/{token:guid}/preview")]
    public async Task<IActionResult> Preview(Guid token)
    {
        var preview = await (
            from l in _db.WaiterSignupLinks
            join r in _db.Restaurants on l.RestaurantId equals r.Id
            join o in _db.Organizations on l.OrganizationId equals o.Id
            where l.Token == token && l.IsActive
            select new WaiterSignupPreviewResponse
            {
                OrganizationName = o.Name,
                RestaurantName = r.Name,
            }).FirstOrDefaultAsync();

        if (preview == null) return NotFound(new { message = "Linkul nu este valid sau a fost revocat." });
        return Ok(preview);
    }

    [AllowAnonymous]
    [HttpPost("api/waiter-signup/{token:guid}")]
    public async Task<IActionResult> Signup(Guid token, [FromBody] WaiterSignupRequest request)
    {
        var link = await _db.WaiterSignupLinks
            .FirstOrDefaultAsync(l => l.Token == token && l.IsActive);
        if (link == null) return NotFound(new { message = "Linkul nu este valid sau a fost revocat." });

        var email = request.Email.Trim().ToLowerInvariant();

        // Idempotent signup: if the email already exists, attach a waiter membership
        // (unless one already exists for this restaurant) and re-send confirmation if
        // the account isn't yet confirmed. Avoids leaking whether the email exists.
        var user = await _users.FindByEmailAsync(email);
        bool createdAccount = false;

        if (user == null)
        {
            user = new AppUser
            {
                UserName = email,
                Email = email,
                FullName = request.FullName.Trim(),
            };
            var result = await _users.CreateAsync(user, request.Password);
            if (!result.Succeeded)
                return BadRequest(new { message = string.Join(" ", result.Errors.Select(e => e.Description)) });
            createdAccount = true;
        }

        var alreadyMember = await _db.Memberships.AnyAsync(m =>
            m.UserId == user.Id &&
            m.RestaurantId == link.RestaurantId &&
            m.Role == MembershipRole.Waiter);

        if (!alreadyMember)
        {
            _db.Memberships.Add(new Membership
            {
                UserId = user.Id,
                OrganizationId = link.OrganizationId,
                RestaurantId = link.RestaurantId,
                Role = MembershipRole.Waiter,
                Status = MembershipStatus.PendingApproval,
                InvitedByUserId = link.CreatedByUserId,
            });
            await _db.SaveChangesAsync();
        }

        if (!user.EmailConfirmed)
            await SendConfirmationEmailAsync(user);

        _logger.LogInformation(
            "Waiter signup: user {UserId} requested access to restaurant {RestaurantId} (createdAccount={Created})",
            user.Id, link.RestaurantId, createdAccount);

        return Ok(new WaiterSignupResultResponse
        {
            Status = "confirmation_sent",
            Email = user.Email ?? email,
            Message = user.EmailConfirmed
                ? "Cererea ta a fost trimisă. Managerul trebuie să o aprobe înainte de a te conecta."
                : "Verifică-ți emailul ca să confirmi contul, apoi așteaptă aprobarea managerului.",
        });
    }

    // ── Manager: roster & approval ───────────────────────────────────────────

    [Authorize]
    [HttpGet("api/restaurants/{restaurantId:guid}/waiters")]
    public async Task<IActionResult> ListByRestaurant(Guid restaurantId, [FromQuery] string? status)
    {
        if (!await _access.CanManageRestaurantAsync(UserId, restaurantId)) return Forbid();

        var query = _db.Memberships
            .Where(m => m.RestaurantId == restaurantId && m.Role == MembershipRole.Waiter);

        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(m => m.Status == status);

        var rows = await query
            .OrderByDescending(m => m.CreatedAt)
            .Join(_db.Users, m => m.UserId, u => u.Id, (m, u) => new { m, u })
            .Join(_db.Restaurants, x => x.m.RestaurantId!.Value, r => r.Id, (x, r) => new WaiterRowResponse
            {
                MembershipId = x.m.Id,
                UserId = x.u.Id,
                FullName = x.u.FullName,
                Email = x.u.Email ?? string.Empty,
                AvatarUrl = x.u.AvatarPath == null ? null : "/" + x.u.AvatarPath,
                RestaurantId = r.Id,
                RestaurantName = r.Name,
                Status = x.m.Status,
                CreatedAt = x.m.CreatedAt,
                EmailConfirmed = x.u.EmailConfirmed,
            })
            .ToListAsync();

        return Ok(rows);
    }

    [Authorize]
    [HttpGet("api/organizations/{orgId:guid}/waiters/pending")]
    public async Task<IActionResult> ListPendingForOrg(Guid orgId)
    {
        if (!await _access.IsOrgOwnerAsync(UserId, orgId)) return Forbid();

        var rows = await _db.Memberships
            .Where(m => m.OrganizationId == orgId &&
                        m.Role == MembershipRole.Waiter &&
                        m.Status == MembershipStatus.PendingApproval)
            .OrderByDescending(m => m.CreatedAt)
            .Join(_db.Users, m => m.UserId, u => u.Id, (m, u) => new { m, u })
            .Join(_db.Restaurants, x => x.m.RestaurantId!.Value, r => r.Id, (x, r) => new WaiterRowResponse
            {
                MembershipId = x.m.Id,
                UserId = x.u.Id,
                FullName = x.u.FullName,
                Email = x.u.Email ?? string.Empty,
                AvatarUrl = x.u.AvatarPath == null ? null : "/" + x.u.AvatarPath,
                RestaurantId = r.Id,
                RestaurantName = r.Name,
                Status = x.m.Status,
                CreatedAt = x.m.CreatedAt,
                EmailConfirmed = x.u.EmailConfirmed,
            })
            .ToListAsync();

        return Ok(rows);
    }

    [Authorize]
    [HttpPost("api/waiters/{membershipId:guid}/approve")]
    public async Task<IActionResult> Approve(Guid membershipId) =>
        await Transition(membershipId, MembershipStatus.Active, requireRestaurantManage: true);

    [Authorize]
    [HttpPost("api/waiters/{membershipId:guid}/reject")]
    public async Task<IActionResult> Reject(Guid membershipId) =>
        await Transition(membershipId, MembershipStatus.Rejected, requireRestaurantManage: true);

    [Authorize]
    [HttpDelete("api/waiters/{membershipId:guid}")]
    public async Task<IActionResult> Remove(Guid membershipId)
    {
        var m = await _db.Memberships.FirstOrDefaultAsync(x => x.Id == membershipId);
        if (m == null) return NotFound();
        if (m.Role != MembershipRole.Waiter || m.RestaurantId is null) return BadRequest();
        if (!await _access.CanManageRestaurantAsync(UserId, m.RestaurantId.Value)) return Forbid();

        _db.Memberships.Remove(m);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    private async Task<IActionResult> Transition(Guid membershipId, string newStatus, bool requireRestaurantManage)
    {
        var m = await _db.Memberships.FirstOrDefaultAsync(x => x.Id == membershipId);
        if (m == null) return NotFound();
        if (m.Role != MembershipRole.Waiter || m.RestaurantId is null) return BadRequest();
        if (requireRestaurantManage && !await _access.CanManageRestaurantAsync(UserId, m.RestaurantId.Value))
            return Forbid();

        m.Status = newStatus;
        await _db.SaveChangesAsync();
        _logger.LogInformation(
            "Waiter membership {MembershipId} transitioned to {Status} by {UserId}",
            membershipId, newStatus, UserId);
        return NoContent();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private WaiterSignupLinkResponse ToResponse(WaiterSignupLink link) => new()
    {
        Token = link.Token,
        SignupPath = $"/waiter-signup/{link.Token}",
        IsActive = link.IsActive,
        CreatedAt = link.CreatedAt,
    };

    private async Task SendConfirmationEmailAsync(AppUser user)
    {
        var raw = await _users.GenerateEmailConfirmationTokenAsync(user);
        var token = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(raw));
        var baseUrl = (_appUrls.AdminBaseUrl ?? "http://localhost:3001").TrimEnd('/');
        var link = $"{baseUrl}/confirm-email?userId={user.Id}&token={token}";

        var html = $"""
            <div style="font-family: system-ui, -apple-system, Segoe UI, Roboto, sans-serif; max-width:520px; margin:0 auto; padding:24px;">
              <h2 style="color:#15140f; margin:0 0 12px;">Confirmă-ți contul</h2>
              <p style="color:#3b3a35; font-size:15px; line-height:1.6; margin:0 0 18px;">
                Bună {System.Net.WebUtility.HtmlEncode(user.FullName)},<br/>
                Mulțumim că te-ai înscris ca ospătar. După confirmarea emailului, managerul va aproba contul tău.
              </p>
              <p style="margin:24px 0;">
                <a href="{link}" style="background:#15140f; color:#fff; text-decoration:none; padding:12px 22px; border-radius:999px; font-weight:600; display:inline-block;">
                  Confirmă emailul
                </a>
              </p>
              <p style="color:#7a7868; font-size:13px; line-height:1.5; margin:0 0 8px;">
                Sau copiază linkul în browser:
              </p>
              <p style="word-break:break-all; font-size:13px; color:#3b3a35; margin:0 0 24px;">
                <a href="{link}" style="color:#3b3a35;">{link}</a>
              </p>
            </div>
            """;

        try
        {
            await _email.SendEmailAsync(user.Email!, "Confirmă-ți contul Smart Restaurant", html);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send waiter confirmation email to {Email}", user.Email);
        }
    }
}
