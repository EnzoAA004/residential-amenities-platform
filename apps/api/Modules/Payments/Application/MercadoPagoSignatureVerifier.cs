using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace ResidentialAmenities.Api.Modules.Payments.Application;

public enum SignatureVerificationResult
{
    Valid,
    MissingSecret,
    MissingSignatureHeader,
    MalformedSignatureHeader,
    MissingTimestamp,
    MissingHash,
    SignatureMismatch,
    TimestampOutOfTolerance
}

/// <summary>
/// Verifies Mercado Pago's <c>x-signature</c> webhook header.
///
/// Algorithm, per the official documentation and the official SDKs'
/// webhook validators (e.g. mercadopago/sdk-go <c>pkg/webhook</c>):
/// <list type="number">
///   <item>The header looks like <c>ts=1742505638683,v1=&lt;hex&gt;</c>.</item>
///   <item>Manifest = <c>id:&lt;data.id&gt;;request-id:&lt;x-request-id&gt;;ts:&lt;ts&gt;;</c>
///     where <c>data.id</c> is the <c>data.id</c> QUERY parameter,
///     lowercased when present; a pair whose value is absent is omitted.</item>
///   <item>v1 = hex(HMAC-SHA256(secret, manifest)), compared in constant time.</item>
/// </list>
/// The signature does NOT cover the body, so callers must take the order id
/// from the signed query parameter only, and must never trust body/query
/// status claims — the order is always re-fetched from the API.
/// </summary>
public static class MercadoPagoSignatureVerifier
{
    /// <summary>The raw <c>ts</c> value of an <c>x-signature</c> header, if present (untrusted until verified).</summary>
    public static string? TryGetTimestamp(string? xSignature)
    {
        foreach (var part in (xSignature ?? string.Empty).Split(','))
        {
            var pair = part.Split('=', 2);

            if (pair.Length == 2 && pair[0].Trim() == "ts")
            {
                return pair[1].Trim();
            }
        }

        return null;
    }

    public static SignatureVerificationResult Verify(
        string? xSignature,
        string? xRequestId,
        string? dataId,
        string? secret,
        DateTimeOffset nowUtc,
        TimeSpan? tolerance = null)
    {
        if (string.IsNullOrWhiteSpace(secret))
        {
            return SignatureVerificationResult.MissingSecret;
        }

        if (string.IsNullOrWhiteSpace(xSignature))
        {
            return SignatureVerificationResult.MissingSignatureHeader;
        }

        string? ts = null;
        string? v1 = null;

        foreach (var part in xSignature.Split(','))
        {
            var pair = part.Split('=', 2);

            if (pair.Length != 2)
            {
                return SignatureVerificationResult.MalformedSignatureHeader;
            }

            switch (pair[0].Trim())
            {
                case "ts":
                    ts = pair[1].Trim();
                    break;
                case "v1":
                    v1 = pair[1].Trim();
                    break;
            }
        }

        if (string.IsNullOrEmpty(ts))
        {
            return SignatureVerificationResult.MissingTimestamp;
        }

        if (string.IsNullOrEmpty(v1))
        {
            return SignatureVerificationResult.MissingHash;
        }

        if (!long.TryParse(ts, NumberStyles.None, CultureInfo.InvariantCulture, out var tsValue))
        {
            return SignatureVerificationResult.MalformedSignatureHeader;
        }

        var manifest = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(dataId))
        {
            manifest.Append("id:").Append(dataId.ToLowerInvariant()).Append(';');
        }

        if (!string.IsNullOrWhiteSpace(xRequestId))
        {
            manifest.Append("request-id:").Append(xRequestId).Append(';');
        }

        manifest.Append("ts:").Append(ts).Append(';');

        byte[] expected;
        using (var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret)))
        {
            expected = hmac.ComputeHash(Encoding.UTF8.GetBytes(manifest.ToString()));
        }

        byte[] provided;
        try
        {
            provided = Convert.FromHexString(v1);
        }
        catch (FormatException)
        {
            return SignatureVerificationResult.MalformedSignatureHeader;
        }

        if (!CryptographicOperations.FixedTimeEquals(expected, provided))
        {
            return SignatureVerificationResult.SignatureMismatch;
        }

        if (tolerance is { } allowed)
        {
            // Docs show both second- and millisecond-resolution timestamps.
            var timestamp = tsValue >= 100_000_000_000
                ? DateTimeOffset.FromUnixTimeMilliseconds(tsValue)
                : DateTimeOffset.FromUnixTimeSeconds(tsValue);

            if ((nowUtc - timestamp).Duration() > allowed)
            {
                return SignatureVerificationResult.TimestampOutOfTolerance;
            }
        }

        return SignatureVerificationResult.Valid;
    }
}
