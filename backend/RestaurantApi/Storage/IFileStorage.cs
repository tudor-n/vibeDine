namespace RestaurantApi.Storage;

public interface IFileStorage
{
    Task<string> WriteAsync(Guid restaurantId, Guid jobId, string fileName, Stream content, CancellationToken ct);
    Task<Stream> OpenAsync(string storageKey, CancellationToken ct);
    Task DeleteAsync(string storageKey, CancellationToken ct);
    // Local impl returns absolute filesystem path; S3 impl downloads to a temp file first and returns its path.
    string ResolveLocalPath(string storageKey);
}
