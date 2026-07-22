namespace RestaurantApi.Services;

/// <summary>
/// Content-based image validation. We never trust the client-declared MIME type;
/// callers sniff the first bytes and compare against a known signature.
/// </summary>
public static class ImageValidation
{
    public static readonly HashSet<string> AllowedImageMimes =
        new(StringComparer.OrdinalIgnoreCase) { "image/png", "image/jpeg", "image/webp" };

    /// <summary>Returns the real MIME from the leading bytes, or null if unrecognised.</summary>
    public static string? SniffImage(ReadOnlySpan<byte> probe)
    {
        // PNG: 89 50 4E 47 0D 0A 1A 0A
        if (probe.Length >= 8 && probe[0] == 0x89 && probe[1] == 0x50 && probe[2] == 0x4E && probe[3] == 0x47)
            return "image/png";
        // JPEG: FF D8 FF
        if (probe.Length >= 3 && probe[0] == 0xFF && probe[1] == 0xD8 && probe[2] == 0xFF)
            return "image/jpeg";
        // WebP: 'RIFF' .... 'WEBP'
        if (probe.Length >= 12 &&
            probe[0] == (byte)'R' && probe[1] == (byte)'I' && probe[2] == (byte)'F' && probe[3] == (byte)'F' &&
            probe[8] == (byte)'W' && probe[9] == (byte)'E' && probe[10] == (byte)'B' && probe[11] == (byte)'P')
            return "image/webp";
        return null;
    }

    public static string ExtensionFor(string mime) => mime switch
    {
        "image/png" => ".png",
        "image/jpeg" => ".jpg",
        "image/webp" => ".webp",
        _ => ".bin",
    };
}
