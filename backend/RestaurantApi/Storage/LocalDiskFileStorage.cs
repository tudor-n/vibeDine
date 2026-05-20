using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace RestaurantApi.Storage;

public sealed class LocalDiskFileStorage : IFileStorage
{
    private readonly string _root;
    private readonly ILogger<LocalDiskFileStorage> _logger;

    public LocalDiskFileStorage(IConfiguration configuration, ILogger<LocalDiskFileStorage> logger)
    {
        _logger = logger;
        _root = Path.GetFullPath(
            configuration["MenuImport:StorageRoot"]
            ?? Path.Combine(Path.GetTempPath(), "menu-imports"));
    }

    /// <inheritdoc />
    /// <returns>Storage key in the format "&lt;restaurantId-N&gt;/&lt;jobId-N&gt;.pdf"</returns>
    public async Task<string> WriteAsync(
        Guid restaurantId,
        Guid jobId,
        string fileName,
        Stream content,
        CancellationToken ct)
    {
        var storageKey = $"{restaurantId:N}/{jobId:N}.pdf";
        var absolutePath = ResolveLocalPath(storageKey);

        Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);

        _logger.LogDebug("Writing file to {Path}", absolutePath);

        await using var fs = new FileStream(
            absolutePath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 81920,
            useAsync: true);

        await content.CopyToAsync(fs, ct);

        return storageKey;
    }

    /// <inheritdoc />
    public Task<Stream> OpenAsync(string storageKey, CancellationToken ct)
    {
        var absolutePath = ResolveLocalPath(storageKey);

        if (!File.Exists(absolutePath))
            throw new FileNotFoundException($"Storage file not found for key '{storageKey}'.", absolutePath);

        Stream stream = new FileStream(
            absolutePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81920,
            useAsync: true);

        return Task.FromResult(stream);
    }

    /// <inheritdoc />
    public Task DeleteAsync(string storageKey, CancellationToken ct)
    {
        var absolutePath = ResolveLocalPath(storageKey);

        if (File.Exists(absolutePath))
        {
            _logger.LogDebug("Deleting file at {Path}", absolutePath);
            File.Delete(absolutePath);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Path-traversal safety: resolves the absolute path and asserts it is rooted
    /// under the configured storage root. Throws <see cref="InvalidOperationException"/>
    /// if a traversal attempt is detected (e.g. "../../etc/passwd").
    /// </remarks>
    public string ResolveLocalPath(string storageKey)
    {
        // Normalise forward-slashes to the OS separator before combining.
        var relativePath = storageKey.Replace('/', Path.DirectorySeparatorChar);
        var candidate = Path.GetFullPath(Path.Combine(_root, relativePath));

        // Ensure the resolved path is strictly under the root (prevent path-traversal).
        // Use OrdinalIgnoreCase on Windows; Ordinal on Unix.
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        if (!candidate.StartsWith(_root + Path.DirectorySeparatorChar, comparison)
            && !string.Equals(candidate, _root, comparison))
        {
            throw new InvalidOperationException("Invalid storage key.");
        }

        return candidate;
    }
}
