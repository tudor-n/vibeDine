using System.Diagnostics;
using System.Text;
using System.Text.Json;
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
            // Never trust the client-declared ContentType: sniff the leading bytes and
            // confirm the file is really an image or PDF before forwarding it upstream.
            if (!IsAllowedScanContent(bytes))
                return BadRequest(new { message = $"Fișierul '{photo.FileName}' nu este o imagine sau un PDF valid." });
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
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The client aborted the request — propagate as a genuine cancellation.
            throw;
        }
        catch (OperationCanceledException ex)
        {
            // Not the client's token → the upstream vision call timed out (6-min budget).
            _logger.LogWarning(ex, "Python /menu/extract timed out");
            return StatusCode(StatusCodes.Status504GatewayTimeout,
                new { message = "Serviciul de scanare nu a răspuns la timp. Încearcă din nou." });
        }
        catch (Exception ex) when (ex is InvalidOperationException or JsonException)
        {
            // Empty/malformed response from the AI service — treat like an outage
            // rather than letting it surface as an opaque 500.
            _logger.LogWarning(ex, "Python /menu/extract returned an unusable response");
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

    // Common HEIF/HEIC brand codes found in the ftyp box of Apple-captured photos.
    private static readonly HashSet<string> HeifBrands = new(StringComparer.OrdinalIgnoreCase)
    {
        "heic", "heix", "heim", "heis", "hevc", "hevx", "hevm", "hevs", "mif1", "msf1", "heif",
    };

    // Content-based check that the bytes are one of the accepted scan formats
    // (PNG/JPEG/WebP via the shared sniffer, plus PDF and HEIC/HEIF) rather than
    // trusting the client's declared ContentType.
    private static bool IsAllowedScanContent(ReadOnlySpan<byte> bytes)
    {
        if (ImageValidation.SniffImage(bytes) is not null)
            return true;

        // PDF: %PDF
        if (bytes.Length >= 4 && bytes[0] == 0x25 && bytes[1] == 0x50 && bytes[2] == 0x44 && bytes[3] == 0x46)
            return true;

        // HEIC/HEIF (ISO-BMFF): "ftyp" box at offset 4, followed by a HEIF brand.
        if (bytes.Length >= 12 &&
            bytes[4] == (byte)'f' && bytes[5] == (byte)'t' && bytes[6] == (byte)'y' && bytes[7] == (byte)'p' &&
            HeifBrands.Contains(Encoding.ASCII.GetString(bytes.Slice(8, 4))))
            return true;

        return false;
    }
}
