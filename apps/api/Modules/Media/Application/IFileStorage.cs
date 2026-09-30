namespace ResidentialAmenities.Api.Modules.Media.Application;

/// <summary>
/// Storage abstraction (issue #92): callers never know or care whether
/// bytes live on local disk, blob storage, or anywhere else. No production
/// cloud provider is decided yet — <c>LocalDiskFileStorage</c> is the only
/// implementation today, and a production one can be swapped in later
/// (registered in DI) without changing any caller.
/// </summary>
public interface IFileStorage
{
    /// <param name="objectKey">
    /// A server-generated key (never a client-supplied filename). Callers
    /// are responsible for generating a safe, collision-resistant key
    /// before calling this.
    /// </param>
    Task SaveAsync(string objectKey, Stream content, CancellationToken cancellationToken);

    /// <returns>
    /// A readable stream for the stored object, or null when no object
    /// exists for that key. Caller owns disposing the stream.
    /// </returns>
    Task<Stream?> OpenReadAsync(string objectKey, CancellationToken cancellationToken);
}
