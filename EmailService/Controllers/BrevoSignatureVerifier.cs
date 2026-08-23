using System.Security.Cryptography;
using System.Text;

namespace EmailService.Controllers;

/// <summary>
/// HMAC-SHA256 verification of Brevo webhook signatures. Pure function so the
/// accept/reject decision can be tested without an HTTP context.
/// </summary>
public static class BrevoSignatureVerifier
{
    /// <summary>
    /// Returns true only when <paramref name="secret"/> is configured and
    /// <paramref name="providedSignature"/> is the hex HMAC-SHA256 of
    /// <paramref name="body"/> under that secret. An absent secret or a
    /// malformed signature is a rejection, never a pass.
    /// </summary>
    public static bool IsValid(string? secret, string body, string? providedSignature)
    {
        if (string.IsNullOrWhiteSpace(secret))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(providedSignature) || !TryParseHex(providedSignature, out var provided))
        {
            return false;
        }

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var expected = hmac.ComputeHash(Encoding.UTF8.GetBytes(body));

        // Fixed-time so response latency does not leak how much of a forged
        // signature was correct.
        return CryptographicOperations.FixedTimeEquals(expected, provided);
    }

    /// <summary>Hex-encodes the HMAC-SHA256 of a body. Used by tests and tooling.</summary>
    public static string ComputeSignature(string secret, string body)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(body))).ToLowerInvariant();
    }

    private static bool TryParseHex(string value, out byte[] bytes)
    {
        bytes = Array.Empty<byte>();

        if (value.Length % 2 != 0)
        {
            return false;
        }

        try
        {
            bytes = Convert.FromHexString(value);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
