using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PlatformApi.Auth;
using PlatformApi.Data;
using PlatformApi.DTO.Requests;
using PlatformApi.DTO.Responses;

namespace PlatformApi.Controllers;

[ApiController]
[Route("api/platform/auth")]
public class AuthController : ControllerBase
{
    private readonly PlatformDbContext _db;
    private readonly IPasswordHasher _hasher;
    private readonly PlatformJwtService _tokens;

    public AuthController(PlatformDbContext db, IPasswordHasher hasher, PlatformJwtService tokens)
    {
        _db = db;
        _hasher = hasher;
        _tokens = tokens;
    }

    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var email = request.Email.Trim().ToLower();
        var admin = await _db.PlatformAdmins.FirstOrDefaultAsync(a => a.Email.ToLower() == email);

        if (admin is null || !admin.IsActive || !_hasher.Verify(request.Password, admin.PasswordHash))
            return Unauthorized(new { message = "Invalid email or password." });

        admin.LastLoginAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        var (token, expiresAt) = _tokens.CreateAccessToken(admin);
        return Ok(new AuthResponse(token, expiresAt, new PlatformAdminDto(admin.Id, admin.Email, admin.FullName)));
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<IActionResult> Me()
    {
        var id = User.PlatformAdminId();
        if (id is null) return Unauthorized();

        var admin = await _db.PlatformAdmins.FindAsync(id.Value);
        if (admin is null || !admin.IsActive) return Unauthorized();

        return Ok(new PlatformAdminDto(admin.Id, admin.Email, admin.FullName));
    }
}
