using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using ResidentialAmenities.Api.Infrastructure.Persistence;
using ResidentialAmenities.Api.Modules.Identity;
using ResidentialAmenities.Api.Modules.Media.Application;
using ResidentialAmenities.Api.Modules.Media.Domain;
using ResidentialAmenities.Api.Modules.Media.Infrastructure;

namespace ResidentialAmenities.Api.Modules.Media;

/// <summary>
/// Safe media upload (issue #92), the prerequisite for #91's incident
/// reports. Deliberately generic (not incident-report-specific): any
/// authenticated resident/administrator can upload an image or video, and
/// only the uploader or an Administrator can ever retrieve it. #91
/// references an uploaded attachment by <see cref="MediaAttachment.Id"/>
/// rather than duplicating any of this.
/// </summary>
public static class MediaModule
{
    private const int HeaderSniffLength = 12;

    public static IServiceCollection AddMediaModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptions<MediaStorageOptions>()
            .Bind(configuration.GetSection(MediaStorageOptions.SectionName));

        services.AddSingleton<IFileStorage, LocalDiskFileStorage>();

        return services;
    }

    public static IEndpointRouteBuilder MapMediaEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints
            .MapGroup("/api/media")
            .WithTags("Media")
            .RequireAuthorization(AuthorizationPolicies.ResidentAccess);

        group.MapPost("/", UploadAsync).DisableAntiforgery();
        group.MapGet("/{id:guid}", DownloadAsync);

        return endpoints;
    }

    private static async Task<IResult> UploadAsync(
        IFormFile? file,
        ClaimsPrincipal principal,
        AppDbContext dbContext,
        IFileStorage storage,
        IOptions<MediaStorageOptions> options,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (principal.GetUserId() is not { } userId)
        {
            return Results.Unauthorized();
        }

        if (file is null || file.Length == 0)
        {
            return ValidationProblem("file", "A file is required.");
        }

        var header = new byte[HeaderSniffLength];
        int bytesRead;

        await using (var probe = file.OpenReadStream())
        {
            bytesRead = await ReadFullyAsync(probe, header, cancellationToken);
        }

        var detectedContentType = AllowedMediaContentTypes.DetectContentType(header.AsSpan(0, bytesRead));

        if (detectedContentType is null)
        {
            return ValidationProblem(
                "file",
                "Unsupported file type. Only JPEG, PNG, WEBP images and MP4, WEBM videos are allowed.");
        }

        var maxSizeBytes = AllowedMediaContentTypes.IsVideo(detectedContentType)
            ? options.Value.MaxVideoSizeBytes
            : options.Value.MaxImageSizeBytes;

        if (file.Length > maxSizeBytes)
        {
            return ValidationProblem(
                "file",
                $"File exceeds the maximum allowed size of {maxSizeBytes} bytes for this content type.");
        }

        // Server-generated, collision-resistant key — the client-supplied
        // filename (file.FileName) is never read, stored, or used to build
        // a path.
        var objectKey = $"{Guid.NewGuid():N}{AllowedMediaContentTypes.FileExtensionFor(detectedContentType)}";

        await using (var uploadStream = file.OpenReadStream())
        {
            await storage.SaveAsync(objectKey, uploadStream, cancellationToken);
        }

        var attachment = new MediaAttachment(
            Guid.NewGuid(),
            userId,
            detectedContentType,
            file.Length,
            objectKey,
            timeProvider.GetUtcNow());

        dbContext.MediaAttachments.Add(attachment);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Results.Created(
            $"/api/media/{attachment.Id}",
            new MediaAttachmentResponse(
                attachment.Id, attachment.ContentType, attachment.SizeBytes, attachment.CreatedAtUtc));
    }

    private static async Task<IResult> DownloadAsync(
        Guid id,
        ClaimsPrincipal principal,
        AppDbContext dbContext,
        IFileStorage storage,
        CancellationToken cancellationToken)
    {
        if (principal.GetUserId() is not { } userId)
        {
            return Results.Unauthorized();
        }

        var attachment = await dbContext.MediaAttachments
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);

        if (attachment is null)
        {
            return Results.NotFound();
        }

        var isOwner = attachment.UploadedByUserId == userId;
        var isAdministrator = principal.IsInRole(ApplicationRoles.Administrator);

        if (!isOwner && !isAdministrator)
        {
            return Results.Problem(
                title: "You do not have access to this file.",
                statusCode: StatusCodes.Status403Forbidden);
        }

        var stream = await storage.OpenReadAsync(attachment.ObjectKey, cancellationToken);

        if (stream is null)
        {
            return Results.NotFound();
        }

        // fileDownloadName forces "Content-Disposition: attachment" (never
        // inline) — the browser is never asked to render/execute this
        // response as part of the page, regardless of content type. The
        // download name is the server-generated object key, never a
        // client-supplied filename (which was never stored).
        return Results.File(stream, attachment.ContentType, fileDownloadName: attachment.ObjectKey);
    }

    private static async Task<int> ReadFullyAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        var totalRead = 0;

        while (totalRead < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(totalRead), cancellationToken);

            if (read == 0)
            {
                break;
            }

            totalRead += read;
        }

        return totalRead;
    }

    private static IResult ValidationProblem(string key, string message) =>
        Results.ValidationProblem(new Dictionary<string, string[]>
        {
            [key] = [message]
        });

    private sealed record MediaAttachmentResponse(
        Guid Id,
        string ContentType,
        long SizeBytes,
        DateTimeOffset CreatedAtUtc);
}
