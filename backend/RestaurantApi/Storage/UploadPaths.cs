namespace RestaurantApi.Storage;

/// <summary>
/// Absolute root directory for user-uploaded assets (branding logo/photo/menu
/// background, avatars, menu-item photos), served back at <c>/uploads/*</c>.
///
/// Defaults to <c>ContentRoot/wwwroot/uploads</c>. Production MUST override it
/// (<c>Uploads__Root</c>) to a path on a persistent volume: PaaS containers
/// (Railway) get a fresh filesystem on every deploy, which silently deletes all
/// uploads while the DB keeps serving their now-dead URLs. The public URL shape
/// (<c>/uploads/…</c>, stored in the DB) never changes with the root.
/// </summary>
public sealed class UploadPaths
{
    public UploadPaths(string root) => Root = Path.GetFullPath(root);

    public string Root { get; }

    /// <summary>Absolute directory for one asset family, e.g. <c>Dir("branding", rid)</c>.</summary>
    public string Dir(params string[] segments) =>
        Path.Combine(Root, Path.Combine(segments));
}
