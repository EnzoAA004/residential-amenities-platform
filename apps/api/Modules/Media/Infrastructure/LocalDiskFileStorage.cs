using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using ResidentialAmenities.Api.Modules.Media.Application;

namespace ResidentialAmenities.Api.Modules.Media.Infrastructure;

/// <summary>
/// Local/development-safe <see cref="IFileStorage"/> (issue #92): files are
/// written under a root directory outside <c>wwwroot</c> and any statically
/// served path, so the only way to ever read one back is through the
/// authenticated <c>GET /api/media/{id}</c> endpoint — static file
/// middleware never has a route to it. Not a production storage choice;
/// swap in a different <see cref="IFileStorage"/> registration for that
/// without changing any caller.
/// </summary>
public sealed class LocalDiskFileStorage : IFileStorage
{
    private readonly string _rootPath;

    public LocalDiskFileStorage(IOptions<MediaStorageOptions> options, IHostEnvironment environment)
    {
        var configuredPath = options.Value.LocalRootPath;
        _rootPath = Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.Combine(environment.ContentRootPath, configuredPath);

        Directory.CreateDirectory(_rootPath);
    }

    public async Task SaveAsync(string objectKey, Stream content, CancellationToken cancellationToken)
    {
        var path = ResolvePath(objectKey);

        await using var file = File.Create(path);
        await content.CopyToAsync(file, cancellationToken);
    }

    public Task<Stream?> OpenReadAsync(string objectKey, CancellationToken cancellationToken)
    {
        var path = ResolvePath(objectKey);

        if (!File.Exists(path))
        {
            return Task.FromResult<Stream?>(null);
        }

        return Task.FromResult<Stream?>(File.OpenRead(path));
    }

    /// <summary>
    /// Object keys are always server-generated (see
    /// <c>MediaModule.UploadAsync</c>), but this still refuses anything that
    /// could escape <see cref="_rootPath"/> (e.g. a key containing "..") as
    /// defense in depth — a bug elsewhere must never turn into a path
    /// traversal.
    /// </summary>
    private string ResolvePath(string objectKey)
    {
        if (string.IsNullOrWhiteSpace(objectKey) ||
            objectKey.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new ArgumentException("Invalid object key.", nameof(objectKey));
        }

        var fullPath = Path.GetFullPath(Path.Combine(_rootPath, objectKey));
        var fullRoot = Path.GetFullPath(_rootPath);

        if (!fullPath.StartsWith(fullRoot, StringComparison.Ordinal))
        {
            throw new ArgumentException("Invalid object key.", nameof(objectKey));
        }

        return fullPath;
    }
}
