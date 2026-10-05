using Azure.Identity;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Options;
using ResidentialAmenities.Api.Modules.Media.Application;

namespace ResidentialAmenities.Api.Modules.Media.Infrastructure;

/// <summary>
/// Cloud file storage backed by a private Azure Blob container.
/// Access uses managed identity through <see cref="DefaultAzureCredential"/>.
/// </summary>
public sealed class AzureBlobFileStorage : IFileStorage
{
    private readonly BlobContainerClient _container;

    public AzureBlobFileStorage(IOptions<MediaStorageOptions> options)
    {
        var settings = options.Value;

        if (string.IsNullOrWhiteSpace(settings.BlobServiceUri))
        {
            throw new InvalidOperationException(
                "MediaStorage:BlobServiceUri is required when MediaStorage:Provider=AzureBlob.");
        }

        if (string.IsNullOrWhiteSpace(settings.BlobContainerName))
        {
            throw new InvalidOperationException(
                "MediaStorage:BlobContainerName is required when MediaStorage:Provider=AzureBlob.");
        }

        var service = new BlobServiceClient(
            new Uri(settings.BlobServiceUri),
            new DefaultAzureCredential());

        _container = service.GetBlobContainerClient(settings.BlobContainerName);
    }

    public async Task SaveAsync(string objectKey, Stream content, CancellationToken cancellationToken)
    {
        var blob = _container.GetBlobClient(objectKey);
        await blob.UploadAsync(content, overwrite: false, cancellationToken);
    }

    public async Task<Stream?> OpenReadAsync(string objectKey, CancellationToken cancellationToken)
    {
        var blob = _container.GetBlobClient(objectKey);

        if (!await blob.ExistsAsync(cancellationToken))
        {
            return null;
        }

        return await blob.OpenReadAsync(cancellationToken: cancellationToken);
    }
}
