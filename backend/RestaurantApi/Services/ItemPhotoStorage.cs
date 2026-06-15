namespace RestaurantApi.Services;

/// <summary>
/// Persists dish-photo crops (decoded from the Python extractor's base64) to
/// <c>wwwroot/uploads/menu-items/{restaurantId:N}/{guid:N}.jpg</c> and returns a
/// root-relative public URL served by the existing static-files middleware.
///
/// Mirrors the avatar storage precedent. Defensive by design: it re-checks the
/// JPEG magic bytes and a 2 MB ceiling and never throws — any rejection or write
/// failure returns null so the import can continue photoless.
/// </summary>
public class ItemPhotoStorage
{
    private const long MaxBytes = 2L * 1024 * 1024;

    private readonly IWebHostEnvironment _env;
    private readonly ILogger<ItemPhotoStorage> _logger;

    public ItemPhotoStorage(IWebHostEnvironment env, ILogger<ItemPhotoStorage> logger)
    {
        _env = env;
        _logger = logger;
    }

    public async Task<string?> SaveAsync(Guid restaurantId, byte[] bytes, CancellationToken ct)
    {
        if (bytes is null || bytes.Length == 0 || bytes.Length > MaxBytes)
            return null;
        // JPEG magic (FF D8 FF). The Python side already re-encodes to JPEG q85;
        // this is a belt-and-braces check against a corrupted/over-the-wire payload.
        if (bytes.Length < 3 || bytes[0] != 0xFF || bytes[1] != 0xD8 || bytes[2] != 0xFF)
            return null;

        try
        {
            var dirFull = Path.Combine(
                _env.ContentRootPath, "wwwroot", "uploads", "menu-items", restaurantId.ToString("N"));
            Directory.CreateDirectory(dirFull);

            var fileName = $"{Guid.NewGuid():N}.jpg";
            var fullPath = Path.Combine(dirFull, fileName);
            await File.WriteAllBytesAsync(fullPath, bytes, ct);

            return $"/uploads/menu-items/{restaurantId:N}/{fileName}";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save item photo for restaurant {RestaurantId}", restaurantId);
            return null;
        }
    }
}
