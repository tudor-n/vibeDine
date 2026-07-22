using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using OpsApi.Auth;
using OpsApi.Data;
using OpsApi.DTO.Requests;
using OpsApi.DTO.Responses;

namespace OpsApi.Controllers;

[ApiController]
[Route("api/ops/auth")]
public sealed class AuthController : ControllerBase
{
    // Fixed decoy hash (same PBKDF2 params as real hashes) used to run an equal-cost verify when the
    // email is unknown, so login timing doesn't reveal whether an account exists. Not a usable credential.
    private const string DummyPasswordHash =
        "100000.+dSvX+c/3ONgiHlLdz0DLA==.gaPm4UnyDkHyl3lJmBPb3e3eCFMbYVDRaOneuzebnIY=";

    private readonly OpsDbContext _db;
    private readonly IPasswordHasher _hasher;
    private readonly OpsJwtService _jwt;

    public AuthController(OpsDbContext db, IPasswordHasher hasher, OpsJwtService jwt)
    {
        _db = db;
        _hasher = hasher;
        _jwt = jwt;
    }

    [HttpPost("login")]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest req)
    {
        var email = req.Email.Trim().ToLowerInvariant();
        var admin = await _db.Admins.FirstOrDefaultAsync(a => a.Email.ToLower() == email);

        // Always run a PBKDF2 verify — against the real hash when the account exists, otherwise against a
        // fixed decoy — so the response time doesn't reveal whether an email is registered.
        var passwordValid = _hasher.Verify(req.Password, admin?.PasswordHash ?? DummyPasswordHash);
        if (admin is null || !admin.IsActive || !passwordValid)
            return Unauthorized(new { message = "Invalid email or password." });

        admin.LastLoginAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        var (token, expiresAt) = _jwt.CreateAccessToken(admin);
        return Ok(new AuthResponse(token, expiresAt, new OpsAdminDto(admin.Id, admin.Email, admin.FullName)));
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<IActionResult> Me()
    {
        var id = User.OpsAdminId();
        if (id is null) return Unauthorized();
        var admin = await _db.Admins.FindAsync(id.Value);
        if (admin is null) return NotFound();
        return Ok(new OpsAdminDto(admin.Id, admin.Email, admin.FullName));
    }
}
