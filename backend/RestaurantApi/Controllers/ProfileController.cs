using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using RestaurantApi.Data;
using RestaurantApi.Models;
using RestaurantApi.Services;

namespace RestaurantApi.Controllers;

/// <summary>
/// Read/write the authenticated user's own profile: display name and avatar.
/// Avatars are stored on disk under <c>wwwroot/uploads/avatars/</c> and served as static files.
/// </summary>
[ApiController]
[Authorize]
[Route("api/auth/me")]
public class ProfileController : ControllerBase
{
    private const long MaxAvatarBytes = 2 * 1024 * 1024; // 2 MB

    private readonly UserManager<AppUser> _users;
    private readonly AppDbContext _db;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<ProfileController> _logger;

    public ProfileController(
        UserManager<AppUser> users,
        AppDbContext db,
        IWebHostEnvironment env,
        ILogger<ProfileController> logger)
    {
        _users = users;
        _db = db;
        _env = env;
        _logger = logger;
    }

    private Guid UserId =>
        Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub")!);

    public class UpdateProfileRequest
    {
        public string? FullName { get; set; }
    }

    public class AvatarResponse
    {
        public string AvatarUrl { get; set; } = string.Empty;
    }

    [HttpPatch]
    public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileRequest request)
    {
        var user = await _users.GetUserAsync(User);
        if (user == null) return Unauthorized();

        if (!string.IsNullOrWhiteSpace(request.FullName))
        {
            user.FullName = request.FullName.Trim();
            await _users.UpdateAsync(user);
        }

        return NoContent();
    }

    [HttpPost("avatar")]
    [RequestSizeLimit(MaxAvatarBytes + 64 * 1024)] // small slack for multipart envelope
    public async Task<IActionResult> UploadAvatar(IFormFile file)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { message = "Fișier lipsă." });
        if (file.Length > MaxAvatarBytes)
            return BadRequest(new { message = "Fișierul depășește 2 MB." });
        if (!ImageValidation.AllowedImageMimes.Contains(file.ContentType))
            return BadRequest(new { message = "Format nepermis. Folosește PNG, JPG sau WebP." });

        // Sniff the first bytes to confirm the declared MIME isn't a renamed extension.
        await using var stream = file.OpenReadStream();
        var probe = new byte[12];
        var read = await stream.ReadAsync(probe.AsMemory(0, probe.Length));
        stream.Position = 0;
        var detected = ImageValidation.SniffImage(probe.AsSpan(0, read));
        if (detected is null || !ImageValidation.AllowedImageMimes.Contains(detected))
            return BadRequest(new { message = "Conținutul fișierului nu este o imagine validă." });

        var ext = ImageValidation.ExtensionFor(detected);

        var user = await _users.GetUserAsync(User);
        if (user == null) return Unauthorized();

        var uploadsRoot = Path.Combine(_env.ContentRootPath, "wwwroot", "uploads", "avatars");
        Directory.CreateDirectory(uploadsRoot);

        // Remove any prior avatar so we don't accumulate dead files.
        if (!string.IsNullOrEmpty(user.AvatarPath))
        {
            var prior = Path.Combine(_env.ContentRootPath, "wwwroot", user.AvatarPath.Replace('/', Path.DirectorySeparatorChar));
            if (System.IO.File.Exists(prior))
            {
                try { System.IO.File.Delete(prior); } catch (Exception ex) { _logger.LogWarning(ex, "Failed to delete old avatar {Path}", prior); }
            }
        }

        var fileName = $"{user.Id:N}{ext}";
        var fullPath = Path.Combine(uploadsRoot, fileName);
        await using (var output = System.IO.File.Create(fullPath))
        {
            await stream.CopyToAsync(output);
        }

        var relative = $"uploads/avatars/{fileName}";
        user.AvatarPath = relative;
        await _users.UpdateAsync(user);

        _logger.LogInformation("User {UserId} uploaded avatar ({Bytes} bytes, {Mime})", user.Id, file.Length, detected);

        return Ok(new AvatarResponse { AvatarUrl = "/" + relative });
    }

    [HttpDelete("avatar")]
    public async Task<IActionResult> DeleteAvatar()
    {
        var user = await _users.GetUserAsync(User);
        if (user == null) return Unauthorized();

        if (!string.IsNullOrEmpty(user.AvatarPath))
        {
            var path = Path.Combine(_env.ContentRootPath, "wwwroot", user.AvatarPath.Replace('/', Path.DirectorySeparatorChar));
            if (System.IO.File.Exists(path))
            {
                try { System.IO.File.Delete(path); } catch (Exception ex) { _logger.LogWarning(ex, "Failed to delete avatar {Path}", path); }
            }
            user.AvatarPath = null;
            await _users.UpdateAsync(user);
        }
        return NoContent();
    }
}
