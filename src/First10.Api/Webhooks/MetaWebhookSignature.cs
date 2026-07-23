using System.Security.Cryptography;
using System.Text;

namespace First10.Api.Webhooks;

/// <summary>
/// Meta webhook authenticity check (R11): X-Hub-Signature-256 is HMAC-SHA256 of the RAW
/// request body keyed with the app secret. Constant-time comparison; anything malformed
/// is invalid. Replay of a captured valid payload is neutralized downstream by the
/// (Channel, ExternalMessageId) dedup index — replays collapse into the original message.
/// </summary>
public sealed class MetaWebhookSignatureValidator(byte[] appSecret)
{
    public bool IsValid(string? signatureHeader, ReadOnlySpan<byte> rawBody)
    {
        if (string.IsNullOrWhiteSpace(signatureHeader)) return false;
        if (!signatureHeader.StartsWith("sha256=", StringComparison.OrdinalIgnoreCase)) return false;

        var provided = signatureHeader["sha256=".Length..].Trim();
        var expected = Convert.ToHexStringLower(HMACSHA256.HashData(appSecret, rawBody));
        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(provided.ToLowerInvariant()));
    }
}

/// <summary>Null Validator = no app secret configured = the webhook path is dead (deny-by-default).</summary>
public sealed record MetaWebhookOptions(MetaWebhookSignatureValidator? Validator);

/// <summary>
/// Rejects any /api/webhooks request whose body is not signed by the configured Meta app
/// secret — before model binding, before Wolverine, before anything. 401 with no detail:
/// an attacker probing the webhook learns nothing (report-forging vector R11). With no
/// secret configured the entire path 401s — a deployment can't accidentally expose an
/// unsigned webhook.
/// </summary>
public sealed class MetaWebhookSignatureMiddleware(RequestDelegate next, MetaWebhookOptions options)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Path.StartsWithSegments("/api/webhooks"))
        {
            await next(context);
            return;
        }

        if (options.Validator is null)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        // Meta's GET verification handshake (hub.challenge) is authenticated by the
        // verify token in the query string, not by a body signature.
        if (HttpMethods.IsGet(context.Request.Method))
        {
            await next(context);
            return;
        }

        context.Request.EnableBuffering();
        using var buffer = new MemoryStream();
        await context.Request.Body.CopyToAsync(buffer);
        context.Request.Body.Position = 0;

        if (!options.Validator.IsValid(context.Request.Headers["X-Hub-Signature-256"], buffer.ToArray()))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        await next(context);
    }
}
