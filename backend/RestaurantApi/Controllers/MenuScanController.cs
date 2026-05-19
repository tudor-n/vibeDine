using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using RestaurantApi.DTOs.Responses;
using RestaurantApi.Services;

namespace RestaurantApi.Controllers;

[ApiController]
[Route("api/admin/menu/scan")]
public class MenuScanController : ControllerBase
{
    private const int MaxFiles = 10;
    private const long MaxFileBytes = 5L * 1024 * 1024;
    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg", "image/png", "image/webp", "image/heic", "application/pdf",
    };

    private readonly MenuExtractionClient _extractor;
    private readonly ILogger<MenuScanController> _logger;

    public MenuScanController(MenuExtractionClient extractor, ILogger<MenuScanController> logger)
    {
        _extractor = extractor;
        _logger = logger;
    }

    [HttpPost]
    [RequestSizeLimit(MaxFiles * MaxFileBytes + 1024 * 1024)]  // 1 MB slack for form overhead
    public async Task<IActionResult> Scan(
        [FromForm(Name = "photos")] List<IFormFile>? photos,
        CancellationToken cancellationToken)
    {
        if (photos is null || photos.Count == 0)
            return BadRequest(new { message = "At least one photo is required." });

        if (photos.Count > MaxFiles)
            return StatusCode(StatusCodes.Status413PayloadTooLarge, new { message = $"At most {MaxFiles} photos allowed." });

        var totalBytes = 0L;
        var prepared = new List<(string FileName, string ContentType, byte[] Bytes)>(photos.Count);
        foreach (var photo in photos)
        {
            if (photo.Length > MaxFileBytes)
                return StatusCode(StatusCodes.Status413PayloadTooLarge,
                    new { message = $"File '{photo.FileName}' exceeds {MaxFileBytes / 1024 / 1024} MB." });
            if (!AllowedContentTypes.Contains(photo.ContentType))
                return BadRequest(new { message = $"Unsupported content type '{photo.ContentType}'." });
            using var ms = new MemoryStream();
            await photo.CopyToAsync(ms, cancellationToken);
            var bytes = ms.ToArray();
            totalBytes += bytes.Length;
            prepared.Add((photo.FileName ?? "photo", photo.ContentType, bytes));
        }

        var sw = Stopwatch.StartNew();
        MenuScanResponse result;
        try
        {
            result = await _extractor.ExtractAsync(prepared, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Python /menu/extract unreachable");
            return StatusCode(StatusCodes.Status502BadGateway,
                new { message = "Serviciul de scanare nu este disponibil. Încearcă din nou." });
        }
        sw.Stop();

        _logger.LogInformation(new EventId(7001, "MenuScan"),
            "MenuScan completed admin={Admin} files={FileCount} bytes={TotalBytes} ms={Duration} model={Model} items={Items} failed={Failed}",
            User.Identity?.Name ?? "anonymous",
            prepared.Count,
            totalBytes,
            sw.ElapsedMilliseconds,
            result.Model,
            result.Items.Count,
            result.FailedPages.Count);

        return Ok(result);
    }
}
