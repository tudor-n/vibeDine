using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using RestaurantApi.Controllers.Requests;
using RestaurantApi.Data;
using RestaurantApi.Models;
using RestaurantApi.Security;
using RestaurantApi.Services;
using RestaurantApi.Storage;
using RestaurantApi.Tenancy;

namespace RestaurantApi.Controllers;

// ── Public admin endpoints ────────────────────────────────────────────────────

[ApiController]
[Authorize]
[Route("api/restaurants/{restaurantId:guid}/admin/menu/imports")]
public class MenuImportController : ControllerBase
{
    private static readonly byte[] PdfMagic = [0x25, 0x50, 0x44, 0x46]; // %PDF

    private readonly AppDbContext _db;
    private readonly IFileStorage _fileStorage;
    private readonly MenuImportOptions _options;
    private readonly ILogger<MenuImportController> _logger;

    public MenuImportController(
        AppDbContext db,
        IFileStorage fileStorage,
        IOptions<MenuImportOptions> options,
        ILogger<MenuImportController> logger)
    {
        _db = db;
        _fileStorage = fileStorage;
        _options = options.Value;
        _logger = logger;
    }

    // POST /api/restaurants/{rid}/admin/menu/imports/pdf
    [HttpPost("pdf")]
    [RequestSizeLimit(110_000_000)]
    public async Task<IActionResult> StartPdfImport(
        Guid restaurantId,
        [FromForm(Name = "pdf")] IFormFile? pdf,
        CancellationToken ct,
        [FromForm(Name = "extractItemPhotos")] bool extractItemPhotos = false)
    {
        if (pdf is null || pdf.Length == 0)
            return BadRequest(new { message = "Fișierul PDF este obligatoriu." });

        if (!string.Equals(pdf.ContentType, "application/pdf", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { message = "Tipul fișierului trebuie să fie application/pdf." });

        if (pdf.Length > _options.MaxFileBytes)
            return BadRequest(new { message = $"Fișierul depășește limita de {_options.MaxFileBytes / 1_000_000} MB." });

        // Magic bytes check
        var header = new byte[4];
        using (var stream = pdf.OpenReadStream())
        {
            var read = await stream.ReadAsync(header, 0, 4, ct);
            if (read < 4 || !header.SequenceEqual(PdfMagic))
                return BadRequest(new { message = "Fișierul nu este un PDF valid." });
        }

        var jobId = Guid.NewGuid();
        string storageKey;
        using (var stream = pdf.OpenReadStream())
        {
            storageKey = await _fileStorage.WriteAsync(restaurantId, jobId, pdf.FileName ?? "upload.pdf", stream, ct);
        }

        var job = new MenuImportJob
        {
            Id = jobId,
            Status = "pending",
            Phase = "parsing",
            OriginalFileName = pdf.FileName ?? "upload.pdf",
            FileSizeBytes = pdf.Length,
            StorageKey = storageKey,
            ProviderUsed = _options.DefaultProvider ?? "gemini",
            ExtractItemPhotos = extractItemPhotos,
        };

        _db.MenuImportJobs.Add(job);
        await _db.SaveChangesAsync(ct);

        return Accepted(new StartPdfImportResponse { JobId = jobId });
    }

    // GET /api/restaurants/{rid}/admin/menu/imports/{jobId}
    [HttpGet("{jobId:guid}")]
    public async Task<IActionResult> GetJob(Guid restaurantId, Guid jobId, CancellationToken ct)
    {
        var job = await _db.MenuImportJobs.FirstOrDefaultAsync(j => j.Id == jobId, ct);
        if (job is null) return NotFound();

        var response = MapToResponse(job);
        return Ok(response);
    }

    // DELETE /api/restaurants/{rid}/admin/menu/imports/{jobId}
    [HttpDelete("{jobId:guid}")]
    public async Task<IActionResult> CancelJob(Guid restaurantId, Guid jobId, CancellationToken ct)
    {
        var job = await _db.MenuImportJobs.FirstOrDefaultAsync(j => j.Id == jobId, ct);
        if (job is null) return NotFound();

        if (string.Equals(job.Status, "complete", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(job.Status, "failed", StringComparison.OrdinalIgnoreCase))
            return Ok();

        var storageKey = job.StorageKey;
        job.Status = "cancelled";
        job.LeaseId = null;
        job.LeaseExpiresAt = null;
        job.LastHeartbeatAt = null;
        job.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        try
        {
            await _fileStorage.DeleteAsync(storageKey, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not delete storage file for cancelled menu import job {JobId}", job.Id);
        }

        return Ok();
    }

    private static MenuImportJobResponse MapToResponse(MenuImportJob job)
    {
        object? result = null;
        if (job.Status == "complete" && job.ResultJson is not null)
        {
            try
            {
                result = JsonSerializer.Deserialize<JsonElement>(job.ResultJson);
            }
            catch
            {
                // Malformed JSON — leave result null
            }
        }

        return new MenuImportJobResponse
        {
            Id = job.Id,
            Status = job.Status,
            Phase = job.Phase,
            TotalPages = job.TotalPages,
            ProcessedPages = job.ProcessedPages,
            FailedPages = job.FailedPages,
            OriginalFileName = job.OriginalFileName,
            FileSizeBytes = job.FileSizeBytes,
            ProviderUsed = job.ProviderUsed,
            Result = result,
            ErrorMessage = job.ErrorMessage,
            CreatedAt = job.CreatedAt,
            UpdatedAt = job.UpdatedAt,
            CompletedAt = job.CompletedAt,
        };
    }
}

// ── Internal progress callback endpoint ───────────────────────────────────────

[ApiController]
[Route("api/internal/menu-imports")]
public class MenuImportInternalController : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private static bool IsTerminalStatus(string status) =>
        string.Equals(status, "cancelled", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, "failed", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, "complete", StringComparison.OrdinalIgnoreCase);

    private readonly AppDbContext _db;
    private readonly ICurrentTenant _tenant;
    private readonly MenuImportOptions _options;
    private readonly MenuImportProgressNotifier _notifier;

    public MenuImportInternalController(
        AppDbContext db,
        ICurrentTenant tenant,
        IOptions<MenuImportOptions> options,
        MenuImportProgressNotifier notifier)
    {
        _db = db;
        _tenant = tenant;
        _options = options.Value;
        _notifier = notifier;
    }

    // POST /api/internal/menu-imports/{jobId}/progress
    [HttpPost("{jobId:guid}/progress")]
    [AllowAnonymous]
    public async Task<IActionResult> ProgressCallback(Guid jobId, [FromQuery] Guid? leaseId, CancellationToken ct)
    {
        // Buffer the body so we can both HMAC-validate and deserialize it.
        Request.EnableBuffering();
        using var ms = new MemoryStream();
        await Request.Body.CopyToAsync(ms, ct);
        var rawBody = ms.ToArray();
        Request.Body.Position = 0;

        // Validate HMAC before doing anything else.
        var signature = Request.Headers["X-Hmac-Signature"].FirstOrDefault();
        if (!HmacRequestValidator.Validate(rawBody, signature, _options.HmacSecret))
            return Unauthorized();

        ProgressCallbackRequest? payload;
        try
        {
            payload = JsonSerializer.Deserialize<ProgressCallbackRequest>(rawBody, JsonOptions);
        }
        catch
        {
            return BadRequest(new { message = "Invalid JSON payload." });
        }

        if (payload is null) return BadRequest(new { message = "Empty payload." });
        if (payload.JobId != jobId)
            return BadRequest(new { message = "Route jobId does not match payload jobId." });

        // Cross-tenant work: suppress tenant filter so we can find the job by ID alone.
        using var _ = _tenant.SuppressResolution();
        var job = await _db.MenuImportJobs
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(j => j.Id == jobId, ct);

        if (job is null) return NotFound();
        if (IsTerminalStatus(job.Status)) return Ok();
        if (leaseId != job.LeaseId) return Ok();

        // Progress callbacks are observational only. The lease-owning background
        // worker is the single writer for terminal complete/failed states.
        var persistedStatus = IsTerminalStatus(payload.Status)
            ? "extracting"
            : payload.Status;

        // Update progress fields
        var now = DateTime.UtcNow;
        job.Status = persistedStatus;
        job.Phase = payload.Phase;
        job.ProcessedPages = payload.ProcessedPages;
        job.TotalPages = payload.TotalPages;
        job.FailedPages = payload.FailedPages;
        job.UpdatedAt = now;
        if (!string.IsNullOrWhiteSpace(payload.ErrorMessage))
        {
            job.ErrorMessage = payload.ErrorMessage;
        }
        else if (!IsTerminalStatus(payload.Status))
        {
            job.ErrorMessage = null;
        }

        await _db.SaveChangesAsync(ct);

        // Emit throttled SignalR progress event
        await _notifier.EmitProgressAsync(job.RestaurantId, job.Id, new
        {
            jobId = job.Id,
            status = persistedStatus,
            phase = payload.Phase,
            processedPages = payload.ProcessedPages,
            totalPages = payload.TotalPages,
            failedPages = payload.FailedPages,
            etaSeconds = payload.EtaSeconds,
            errorMessage = payload.ErrorMessage,
        }, ct);

        return Ok();
    }
}
