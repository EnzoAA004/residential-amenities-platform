using System.Security.Cryptography;
using System.Text;

namespace ResidentialAmenities.Api.Tests.Infrastructure;

/// <summary>Builds a Mercado Pago style x-signature exactly as the official algorithm defines it.</summary>
public static class WebhookSigner
{
    public static string Manifest(string? dataId, string? requestId, string ts)
    {
        var manifest = new StringBuilder();

        if (!string.IsNullOrEmpty(dataId))
        {
            manifest.Append("id:").Append(dataId.ToLowerInvariant()).Append(';');
        }

        if (!string.IsNullOrEmpty(requestId))
        {
            manifest.Append("request-id:").Append(requestId).Append(';');
        }

        manifest.Append("ts:").Append(ts).Append(';');
        return manifest.ToString();
    }

    public static string Hash(string secret, string manifest)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(manifest)))
            .ToLowerInvariant();
    }

    public static string Header(string secret, string? dataId, string? requestId, string ts) =>
        $"ts={ts},v1={Hash(secret, Manifest(dataId, requestId, ts))}";
}
