namespace ResidentialAmenities.Api.Modules.Media.Application;

/// <summary>
/// Media storage configuration, bound from the <c>MediaStorage</c> section.
/// No production cloud storage provider is decided yet (that would cross
/// into Phase 7) — <see cref="LocalRootPath"/> backs the local/dev-safe
/// <c>LocalDiskFileStorage</c> only. A production <c>IFileStorage</c>
/// implementation can be swapped in later without changing any endpoint.
/// </summary>
public sealed class MediaStorageOptions
{
    public const string SectionName = "MediaStorage";

    /// <summary>
    /// Relative to the app's content root unless already absolute.
    /// Deliberately outside <c>wwwroot</c>/any statically-served directory —
    /// files are only ever reachable through the authenticated
    /// <c>GET /api/media/{id}</c> endpoint, never served directly.
    /// </summary>
    public string LocalRootPath { get; set; } = "App_Data/media";

    /// <summary>Maximum accepted size for an image upload, in bytes. Default 10 MB.</summary>
    public long MaxImageSizeBytes { get; set; } = 10 * 1024 * 1024;

    /// <summary>Maximum accepted size for a video upload, in bytes. Default 100 MB.</summary>
    public long MaxVideoSizeBytes { get; set; } = 100 * 1024 * 1024;
}
