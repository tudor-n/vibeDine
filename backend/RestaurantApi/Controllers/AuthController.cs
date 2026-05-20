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
using System.Text;

namespace RestaurantApi.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly UserManager<AppUser> _users;
    private readonly AppDbContext _db;
    private readonly JwtTokenService _tokens;
    private readonly IEmailSender _email;
    private readonly AppUrlOptions _appUrls;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        UserManager<AppUser> users,
        AppDbContext db,
        JwtTokenService tokens,
        IEmailSender email,
        IOptions<AppUrlOptions> appUrls,
        ILogger<AuthController> logger)
    {
        _users = users;
        _db = db;
        _tokens = tokens;
        _email = email;
        _appUrls = appUrls.Value;
        _logger = logger;
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        var existing = await _users.FindByEmailAsync(request.Email);
        if (existing != null)
        {
            // If the prior signup attempt never confirmed, treat this as a resend
            // rather than leaking that the address is registered.
            if (!existing.EmailConfirmed)
            {
                await SendConfirmationEmailAsync(existing);
                return Ok(new RegisterResponse
                {
                    Status = "confirmation_sent",
                    Email = existing.Email ?? request.Email,
                    Message = "Ți-am trimis un nou link de confirmare pe email.",
                });
            }
            return Conflict(new { message = "An account with this email already exists." });
        }

        var user = new AppUser
        {
            UserName = request.Email,
            Email = request.Email,
            FullName = request.FullName,
        };

        var result = await _users.CreateAsync(user, request.Password);
        if (!result.Succeeded)
            return BadRequest(new { message = string.Join(" ", result.Errors.Select(e => e.Description)) });

        await SendConfirmationEmailAsync(user);

        _logger.LogInformation("Registered user {UserId} ({Email}) — confirmation email sent", user.Id, user.Email);
        return Ok(new RegisterResponse
        {
            Status = "confirmation_sent",
            Email = user.Email ?? request.Email,
            Message = "Verifică-ți emailul pentru a finaliza crearea contului.",
        });
    }

    [HttpPost("confirm-email")]
    public async Task<IActionResult> ConfirmEmail([FromBody] ConfirmEmailRequest request)
    {
        var user = await _users.FindByIdAsync(request.UserId.ToString());
        if (user == null)
            return BadRequest(new { message = "Link de confirmare invalid." });

        if (user.EmailConfirmed)
        {
            // Idempotent: allow the user to log in via the same flow.
            return Ok(await BuildAuthResponseAsync(user));
        }

        string decoded;
        try
        {
            decoded = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(request.Token));
        }
        catch
        {
            return BadRequest(new { message = "Link de confirmare invalid sau expirat." });
        }

        var result = await _users.ConfirmEmailAsync(user, decoded);
        if (!result.Succeeded)
            return BadRequest(new { message = "Link de confirmare invalid sau expirat." });

        _logger.LogInformation("User {UserId} ({Email}) confirmed email", user.Id, user.Email);
        return Ok(await BuildAuthResponseAsync(user));
    }

    [HttpPost("resend-confirmation")]
    public async Task<IActionResult> ResendConfirmation([FromBody] ResendConfirmationRequest request)
    {
        var user = await _users.FindByEmailAsync(request.Email);
        // Never disclose whether the email exists.
        if (user != null && !user.EmailConfirmed)
            await SendConfirmationEmailAsync(user);

        return Ok(new { message = "Dacă există un cont neconfirmat cu acest email, am trimis un nou link." });
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var user = await _users.FindByEmailAsync(request.Email);
        if (user == null || !await _users.CheckPasswordAsync(user, request.Password))
            return Unauthorized(new { message = "Invalid email or password." });

        if (!user.EmailConfirmed)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                code = "email_not_confirmed",
                email = user.Email,
                message = "Confirmă-ți adresa de email înainte de autentificare.",
            });
        }

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

    private async Task SendConfirmationEmailAsync(AppUser user)
    {
        var raw = await _users.GenerateEmailConfirmationTokenAsync(user);
        // Identity tokens contain characters that don't survive a URL query string,
        // so encode them with the URL-safe base64 variant before sending.
        var token = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(raw));

        var baseUrl = (_appUrls.AdminBaseUrl ?? "http://localhost:3001").TrimEnd('/');
        var link = $"{baseUrl}/confirm-email?userId={user.Id}&token={token}";

        var html = $"""
            <div style="font-family: system-ui, -apple-system, Segoe UI, Roboto, sans-serif; max-width:520px; margin:0 auto; padding:24px;">
              <h2 style="color:#15140f; margin:0 0 12px;">Confirmă-ți contul</h2>
              <p style="color:#3b3a35; font-size:15px; line-height:1.6; margin:0 0 18px;">
                Bună {System.Net.WebUtility.HtmlEncode(user.FullName)},<br/>
                Mulțumim că te-ai înregistrat la Smart Restaurant. Apasă butonul de mai jos pentru a finaliza crearea contului.
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
              <p style="color:#9c9a8c; font-size:12px; margin:0;">
                Dacă nu tu ai cerut acest cont, ignoră acest email.
              </p>
            </div>
            """;

        try
        {
            await _email.SendEmailAsync(user.Email!, "Confirmă-ți contul Smart Restaurant", html);
        }
        catch (Exception ex)
        {
            // Don't leak SMTP failures to the client, but log loudly so dev can diagnose.
            _logger.LogError(ex, "Failed to send confirmation email to {Email}", user.Email);
        }
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
