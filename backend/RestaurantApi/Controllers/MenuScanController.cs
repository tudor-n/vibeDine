using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RestaurantApi.DTOs.Responses;
using RestaurantApi.Services;

namespace RestaurantApi.Controllers;

[ApiController]
[Authorize]
[Route("api/restaurants/{restaurantId:guid}/admin/menu/scan")]
public class MenuScanController : ControllerBase
{
    private const int MaxFiles = 10;
    private const long MaxFileBytes = 5L * 1024 * 1024;
    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg", "image/png", "image/webp", "image/heic", "application/pdf",
    };

    private readonly MenuExtractionClient _extractor;
    private readonly ItemPhotoStorage _photoStorage;
    private readonly ILogger<MenuScanController> _logger;

    public MenuScanController(
        MenuExtractionClient extractor,
        ItemPhotoStorage photoStorage,
        ILogger<MenuScanController> logger)
    {
        _extractor = extractor;
        _photoStorage = photoStorage;
        _logger = logger;
    }

    [HttpPost]
    [RequestSizeLimit(MaxFiles * MaxFileBytes + 1024 * 1024)]  // 1 MB slack for form overhead
    public async Task<IActionResult> Scan(
        Guid restaurantId,
        [FromForm(Name = "photos")] List<IFormFile>? photos,
        CancellationToken cancellationToken,
        [FromForm(Name = "locale")] string locale = "ro-RO",
        [FromForm(Name = "extractItemPhotos")] bool extractItemPhotos = false)
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
        MenuScanResult scan;
        try
        {
            scan = await _extractor.ExtractWithPhotosAsync(prepared, cancellationToken, locale, extractItemPhotos);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Python /menu/extract unreachable");
            return StatusCode(StatusCodes.Status502BadGateway,
                new { message = "Serviciul de scanare nu este disponibil. Încearcă din nou." });
        }
        sw.Stop();

        var result = scan.Response;
        // Persist any dish-photo crops and stamp their URLs onto matching items.
        // The whole merge is best-effort: a failure here must never fail the scan,
        // it just returns items without photos.
        if (extractItemPhotos && scan.ItemPhotos.Count > 0)
        {
            try
            {
                var urlByRef = new Dictionary<(int Page, int Index), string>();
                foreach (var photo in scan.ItemPhotos)
                {
                    byte[] bytes;
                    try { bytes = Convert.FromBase64String(photo.ImageBase64); }
                    catch (FormatException) { continue; }
                    var url = await _photoStorage.SaveAsync(restaurantId, bytes, cancellationToken);
                    if (url is not null)
                        urlByRef[(photo.SourcePage, photo.SourceIndex)] = url;
                }
                if (urlByRef.Count > 0)
                {
                    var stamped = result.Items
                        .Select(it => urlByRef.TryGetValue((it.SourcePage, it.SourceIndex), out var url)
                            ? it with { ImageUrl = url }
                            : it)
                        .ToList();
                    result = result with { Items = stamped };
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Item photo merge failed for restaurant {RestaurantId}; continuing photoless", restaurantId);
            }
        }

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
