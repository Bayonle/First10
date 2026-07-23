using First10.Infrastructure.Media;

namespace First10.Tests;

/// <summary>
/// Signed media URLs (§7.1/D-012): the signature is the serve authorization —
/// valid within its lifetime, dead after expiry, dead if any part is tampered with.
/// </summary>
public class MediaAccessTests
{
    private static readonly byte[] Key = "unit-test-signing-key"u8.ToArray();
    private readonly MediaUrlSigner _signer = new(Key, TimeSpan.FromMinutes(5));

    [Fact]
    public void Issued_url_validates_within_lifetime()
    {
        var now = DateTimeOffset.UtcNow;
        var (expires, sig) = _signer.Issue("abc123.jpg", now);

        Assert.True(_signer.Validate("abc123.jpg", expires, sig, now));
        Assert.True(_signer.Validate("abc123.jpg", expires, sig, now.AddMinutes(4.9)));
    }

    [Fact]
    public void Expired_url_is_rejected()
    {
        var now = DateTimeOffset.UtcNow;
        var (expires, sig) = _signer.Issue("abc123.jpg", now);

        Assert.False(_signer.Validate("abc123.jpg", expires, sig, now.AddMinutes(6)));
    }

    [Fact]
    public void Tampered_media_ref_or_expiry_is_rejected()
    {
        var now = DateTimeOffset.UtcNow;
        var (expires, sig) = _signer.Issue("abc123.jpg", now);

        // Swap the ref: a signature for one asset must not open another.
        Assert.False(_signer.Validate("other-asset.jpg", expires, sig, now));
        // Extend the expiry: signature no longer matches.
        Assert.False(_signer.Validate("abc123.jpg", expires + 3600, sig, now));
        // Garbage signature.
        Assert.False(_signer.Validate("abc123.jpg", expires, "deadbeef", now));
    }

    [Fact]
    public void Different_keys_produce_incompatible_signatures()
    {
        var now = DateTimeOffset.UtcNow;
        var other = new MediaUrlSigner("a-different-key"u8.ToArray(), TimeSpan.FromMinutes(5));
        var (expires, sig) = other.Issue("abc123.jpg", now);

        Assert.False(_signer.Validate("abc123.jpg", expires, sig, now));
    }
}
