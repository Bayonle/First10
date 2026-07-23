using System.Security.Cryptography;
using System.Text;
using First10.Api.Webhooks;

namespace First10.Tests;

/// <summary>
/// R11 webhook gate: only payloads signed with the Meta app secret get through.
/// Replay of a byte-identical valid payload passes the signature (Meta's scheme has no
/// timestamp) and is then collapsed by the (Channel, ExternalMessageId) dedup index —
/// that pairing, not the signature, is the replay defense, and it's tested in the
/// ingest dedup suite.
/// </summary>
public class WebhookSignatureTests
{
    private static readonly byte[] Secret = "meta-app-secret-under-test"u8.ToArray();
    private readonly MetaWebhookSignatureValidator _validator = new(Secret);

    private static string Sign(byte[] body, byte[] secret) =>
        "sha256=" + Convert.ToHexStringLower(HMACSHA256.HashData(secret, body));

    [Fact]
    public void Correctly_signed_payload_is_valid()
    {
        var body = Encoding.UTF8.GetBytes("""{"entry":[{"changes":[{"value":{"messages":[]}}]}]}""");
        Assert.True(_validator.IsValid(Sign(body, Secret), body));
    }

    [Fact]
    public void Uppercase_hex_signature_is_accepted()
    {
        // Some SDKs emit uppercase hex; the spec is case-insensitive on the digest.
        var body = Encoding.UTF8.GetBytes("payload");
        var upper = "sha256=" + Convert.ToHexString(HMACSHA256.HashData(Secret, body));
        Assert.True(_validator.IsValid(upper, body));
    }

    [Fact]
    public void Tampered_body_is_rejected()
    {
        var body = Encoding.UTF8.GetBytes("""{"messages":["original report"]}""");
        var signature = Sign(body, Secret);
        var tampered = Encoding.UTF8.GetBytes("""{"messages":["forged report"]}""");

        Assert.False(_validator.IsValid(signature, tampered));
    }

    [Fact]
    public void Signature_from_the_wrong_secret_is_rejected()
    {
        var body = Encoding.UTF8.GetBytes("payload");
        Assert.False(_validator.IsValid(Sign(body, "attacker-guessed-secret"u8.ToArray()), body));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("sha256=")]
    [InlineData("sha1=abc123")] // Meta's legacy header — not acceptable
    [InlineData("deadbeef")]
    public void Missing_or_malformed_headers_are_rejected(string? header)
    {
        Assert.False(_validator.IsValid(header, "payload"u8));
    }
}
