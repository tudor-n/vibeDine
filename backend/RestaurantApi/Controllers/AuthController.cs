using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RestaurantApi.Auth;
using RestaurantApi.Data;
using RestaurantApi.DTOs.Requests;
using RestaurantApi.DTOs.Responses;
using RestaurantApi.Models;

namespace RestaurantApi.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly UserManager<AppUser> _users;
    private readonly AppDbContext _db;
    private readonly JwtTokenService _tokens;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        UserManager<AppUser> users,
        AppDbContext db,
        JwtTokenService tokens,
        ILogger<AuthController> logger)
    {
        _users = users;
        _db = db;
        _tokens = tokens;
        _logger = logger;
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        var existing = await _users.FindByEmailAsync(request.Email);
        if (existing != null)
            return Conflict(new { message = "An account with this email already exists." });

        var user = new AppUser
        {
            UserName = request.Email,
            Email = request.Email,
            FullName = request.FullName,
        };

        var result = await _users.CreateAsync(user, request.Password);
        if (!result.Succeeded)
            return BadRequest(new { message = string.Join(" ", result.Errors.Select(e => e.Description)) });

        _logger.LogInformation("Registered user {UserId} ({Email})", user.Id, user.Email);
        return Ok(await BuildAuthResponseAsync(user));
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var user = await _users.FindByEmailAsync(request.Email);
        if (user == null || !await _users.CheckPasswordAsync(user, request.Password))
            return Unauthorized(new { message = "Invalid email or password." });

        return Ok(await BuildAuthResponseAsync(user));
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh([FromBody] RefreshRequest request)
    {
        var stored = await _db.RefreshTokens
            .FirstOrDefaultAsync(t => t.Token == request.RefreshToken);

        if (stored == null || !stored.IsActive)
            return Unauthorized(new { message = "Invalid or expired refresh token." });

        var user = await _users.FindByIdAsync(stored.UserId.ToString());
        if (user == null)
            return Unauthorized(new { message = "Account no longer exists." });

        // Rotate: revoke the used token, issue a fresh pair.
        stored.RevokedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return Ok(await BuildAuthResponseAsync(user));
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<IActionResult> Me()
    {
        var user = await _users.GetUserAsync(User);
        if (user == null) return Unauthorized();
        return Ok(await BuildProfileAsync(user));
    }

    /// <summary>
    /// Accept a co-owner invitation. The caller must be authenticated and their email
    /// must match the invite. Creates the RestaurantAdmin membership.
    /// </summary>
    [Authorize]
    [HttpPost("accept-invite")]
    public async Task<IActionResult> AcceptInvite([FromBody] AcceptInviteRequest request)
    {
        var user = await _users.GetUserAsync(User);
        if (user == null) return Unauthorized();

        var invite = await _db.Invitations.FirstOrDefaultAsync(i => i.Token == request.Token);
        if (invite == null) return NotFound(new { message = "Invitation not found." });

        if (invite.Status != "pending" || invite.ExpiresAt < DateTime.UtcNow)
            return BadRequest(new { message = "This invitation is no longer valid." });

        if (!string.Equals(invite.Email, user.Email, StringComparison.OrdinalIgnoreCase))
            return Forbid();

        var already = await _db.Memberships.AnyAsync(m =>
            m.UserId == user.Id &&
            m.OrganizationId == invite.OrganizationId &&
            m.RestaurantId == invite.RestaurantId);

        if (!already)
        {
            _db.Memberships.Add(new Membership
            {
                UserId = user.Id,
                OrganizationId = invite.OrganizationId,
                RestaurantId = invite.RestaurantId,
                Role = invite.Role,
                InvitedByUserId = invite.InvitedByUserId,
            });
        }

        invite.Status = "accepted";
        await _db.SaveChangesAsync();

        _logger.LogInformation("User {UserId} accepted invite {InviteId}", user.Id, invite.Id);
        return Ok(await BuildProfileAsync(user));
    }

    private async Task<AuthResponse> BuildAuthResponseAsync(AppUser user)
    {
        var (token, expiresAt) = _tokens.CreateAccessToken(user);
        var refresh = _tokens.CreateRefreshToken(user.Id);
        _db.RefreshTokens.Add(refresh);
        await _db.SaveChangesAsync();

        return new AuthResponse
        {
            AccessToken = token,
            ExpiresAt = expiresAt,
            RefreshToken = refresh.Token,
            User = await BuildProfileAsync(user),
        };
    }

    private async Task<UserProfileResponse> BuildProfileAsync(AppUser user)
    {
        var memberships = await _db.Memberships
            .Where(m => m.UserId == user.Id)
            .Join(_db.Organizations, m => m.OrganizationId, o => o.Id, (m, o) => new { m, o })
            .Select(x => new MembershipDto
            {
                OrganizationId = x.o.Id,
                OrganizationName = x.o.Name,
                RestaurantId = x.m.RestaurantId,
                RestaurantName = x.m.RestaurantId == null
                    ? null
                    : _db.Restaurants.Where(r => r.Id == x.m.RestaurantId).Select(r => r.Name).FirstOrDefault(),
                Role = x.m.Role.ToString(),
            })
            .ToListAsync();

        return new UserProfileResponse
        {
            Id = user.Id,
            Email = user.Email ?? string.Empty,
            FullName = user.FullName,
            Memberships = memberships,
        };
    }
}
