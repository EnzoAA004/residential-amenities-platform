namespace ResidentialAmenities.Api.Modules.Media.Application;

/// <summary>
/// The strict allowlist for issue #92: exactly these five content types are
/// ever accepted, detected by inspecting the file's actual leading bytes
/// (a "magic number" signature) rather than trusting the client-supplied
/// <c>Content-Type</c> header or filename extension, which are both easy to
/// spoof. Anything that doesn't match one of these signatures — including
/// scripts, HTML, active/scriptable SVG or any executable — is rejected.
/// </summary>
public static class AllowedMediaContentTypes
{
    public const string ImageJpeg = "image/jpeg";
    public const string ImagePng = "image/png";
    public const string ImageWebp = "image/webp";
    public const string VideoMp4 = "video/mp4";
    public const string VideoWebm = "video/webm";

    private static readonly HashSet<string> ImageTypes =
        new(StringComparer.Ordinal) { ImageJpeg, ImagePng, ImageWebp };

    private static readonly HashSet<string> VideoTypes =
        new(StringComparer.Ordinal) { VideoMp4, VideoWebm };

    public static bool IsImage(string contentType) => ImageTypes.Contains(contentType);

    public static bool IsVideo(string contentType) => VideoTypes.Contains(contentType);

    /// <summary>
    /// Returns the detected content type from the file's leading bytes, or
    /// null when the header matches none of the allowed signatures.
    /// </summary>
    public static string? DetectContentType(ReadOnlySpan<byte> header)
    {
        // JPEG: FF D8 FF
        if (header.Length >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
        {
            return ImageJpeg;
        }

        // PNG: 89 50 4E 47 0D 0A 1A 0A
        if (header.Length >= 8 &&
            header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47 &&
            header[4] == 0x0D && header[5] == 0x0A && header[6] == 0x1A && header[7] == 0x0A)
        {
            return ImagePng;
        }

        // WEBP: "RIFF" .... "WEBP"
        if (header.Length >= 12 &&
            header[0] == (byte)'R' && header[1] == (byte)'I' && header[2] == (byte)'F' && header[3] == (byte)'F' &&
            header[8] == (byte)'W' && header[9] == (byte)'E' && header[10] == (byte)'B' && header[11] == (byte)'P')
        {
            return ImageWebp;
        }

        // MP4 (ISO base media file format): bytes 4-7 are "ftyp"
        if (header.Length >= 8 &&
            header[4] == (byte)'f' && header[5] == (byte)'t' && header[6] == (byte)'y' && header[7] == (byte)'p')
        {
            return VideoMp4;
        }

        // WEBM (EBML header): 1A 45 DF A3
        if (header.Length >= 4 &&
            header[0] == 0x1A && header[1] == 0x45 && header[2] == 0xDF && header[3] == 0xA3)
        {
            return VideoWebm;
        }

        return null;
    }

    public static string FileExtensionFor(string contentType) => contentType switch
    {
        ImageJpeg => ".jpg",
        ImagePng => ".png",
        ImageWebp => ".webp",
        VideoMp4 => ".mp4",
        VideoWebm => ".webm",
        _ => throw new ArgumentOutOfRangeException(nameof(contentType), contentType, "Unsupported content type.")
    };
}
