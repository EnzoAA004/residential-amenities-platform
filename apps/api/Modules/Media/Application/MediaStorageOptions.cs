namespace ResidentialAmenities.Api.Modules.Media.Application;

/// <summary>
/// Media storage configuration, bound from the <c>MediaStorage</c> section.
/// Local development uses disk storage; Azure deployments use private Blob
/// Storage through managed identity.
/// </summary>
public sealed class MediaStorageOptions
{
    public const string SectionName = "MediaStorage";

    /// <summary>Supported values: <c>LocalDisk</c> and <c>AzureBlob</c>.</summary>
    public string Provider { get; set; } = "LocalDisk";

    /// <summary>Relative to the content root unless absolute. Used only by LocalDisk.</summary>
    public string LocalRootPath { get; set; } = "App_Data/media";

    /// <summary>Azure Blob service URI. Authentication uses managed identity.</summary>
    public string BlobServiceUri { get; set; } = string.Empty;

    /// <summary>Private Blob container used for uploaded media.</summary>
    public string BlobContainerName { get; set; } = "media";

    /// <summary>Maximum accepted size for an image upload, in bytes. Default 10 MB.</summary>
    public long MaxImageSizeBytes { get; set; } = 10 * 1024 * 1024;

    /// <summary>Maximum accepted size for a video upload, in bytes. Default 100 MB.</summary>
    public long MaxVideoSizeBytes { get; set; } = 100 * 1024 * 1024;
}
