using System.Security.Cryptography;
using System.Text;

namespace ResidentialAmenities.Api.Modules.Identity.Application;

/// <summary>
/// Generates and hashes the 6-digit numeric codes used by issue #93. The
/// plaintext code is only ever held in memory long enough to email it —
/// <see cref="VerificationCode"/>-equivalent database rows store only the
/// hash produced by <see cref="Hash"/>.
/// </summary>
public static class VerificationCodeGenerator
{
    public const int CodeLength = 6;

    public static string GenerateCode()
    {
        // A uniform 6-digit code (000000-999999), including leading zeros —
        // rejection sampling avoids modulo bias.
        Span<byte> buffer = stackalloc byte[4];
        uint max = 1_000_000;
        uint limit = uint.MaxValue - (uint.MaxValue % max) - 1;
        uint value;

        do
        {
            RandomNumberGenerator.Fill(buffer);
            value = BitConverter.ToUInt32(buffer);
        }
        while (value > limit);

        return (value % max).ToString($"D{CodeLength}");
    }

    public static string Hash(string code)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(code));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
