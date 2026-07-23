using System.Security.Cryptography;
using System.Text;

namespace First10.Infrastructure.Media;

/// <summary>
/// HMAC-signed, short-lived media URLs (D-012 / §7.1). The signature is the authorization:
/// the serve endpoint accepts nothing without a valid, unexpired signature, and issuance —
/// the moment a URL is minted for a person — is what the access log records.
/// </summary>
public sealed class MediaUrlSigner(byte[] key, TimeSpan lifetime)
{
    public TimeSpan Lifetime => lifetime;

    public (long ExpiresUnix, string Signature) Issue(string mediaRef, DateTimeOffset now)
    {
        var expires = now.Add(lifetime).ToUnixTimeSeconds();
        return (expires, Compute(mediaRef, expires));
    }

    public bool Validate(string mediaRef, long expiresUnix, string signature, DateTimeOffset now)
    {
        if (now.ToUnixTimeSeconds() > expiresUnix) return false;
        var expected = Compute(mediaRef, expiresUnix);
        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(signature));
    }

    private string Compute(string mediaRef, long expiresUnix) =>
        Convert.ToHexStringLower(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes($"{mediaRef}:{expiresUnix}")));
}
