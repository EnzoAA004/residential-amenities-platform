namespace ResidentialAmenities.Api.Modules.Media.Domain;

/// <summary>
/// A single uploaded file (issue #92 — prerequisite for #91's incident
/// reports). Deliberately minimal: the actual bytes live behind
/// <c>IFileStorage</c> under <see cref="ObjectKey"/>, a server-generated,
/// collision-resistant key — never the client-supplied filename, which is
/// discarded entirely and never stored or trusted for anything (path
/// construction, content type, or display).
/// </summary>
public sealed class MediaAttachment
{
    private MediaAttachment()
    {
    }

    public MediaAttachment(
        Guid id,
        Guid uploadedByUserId,
        string contentType,
        long sizeBytes,
        string objectKey,
        DateTimeOffset createdAtUtc)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Media attachment id is required.", nameof(id));
        }

        if (uploadedByUserId == Guid.Empty)
        {
            throw new ArgumentException("Uploader user id is required.", nameof(uploadedByUserId));
        }

        if (string.IsNullOrWhiteSpace(contentType))
        {
            throw new ArgumentException("Content type is required.", nameof(contentType));
        }

        if (sizeBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sizeBytes), "Size must be positive.");
        }

        if (string.IsNullOrWhiteSpace(objectKey))
        {
            throw new ArgumentException("Object key is required.", nameof(objectKey));
        }

        Id = id;
        UploadedByUserId = uploadedByUserId;
        ContentType = contentType;
        SizeBytes = sizeBytes;
        ObjectKey = objectKey;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; private set; }

    public Guid UploadedByUserId { get; private set; }

    public string ContentType { get; private set; } = string.Empty;

    public long SizeBytes { get; private set; }

    public string ObjectKey { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; private set; }
}
