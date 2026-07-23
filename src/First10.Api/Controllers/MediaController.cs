using First10.Domain.Abstractions;
using First10.Infrastructure.Media;
using Microsoft.AspNetCore.Mvc;

namespace First10.Api.Controllers;

/// <summary>
/// Serves stored media to the console — only via short-lived signed URLs (D-012 / §7.1).
/// URLs are minted (and access-logged with who + incident) when a ticket timeline is
/// fetched; this endpoint verifies the HMAC and expiry and serves bytes, nothing more.
/// An unsigned or expired request gets 403 regardless of whether the media exists.
/// </summary>
[ApiController]
[Microsoft.AspNetCore.Authorization.AllowAnonymous] // the HMAC signature IS the authorization
[Route("api/media")]
public class MediaController(IMediaStore mediaStore, MediaUrlSigner signer) : ControllerBase
{
    [HttpGet("{mediaRef}")]
    public async Task<IActionResult> Get(string mediaRef, [FromQuery] long e, [FromQuery] string? s, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(s) || !signer.Validate(mediaRef, e, s, DateTimeOffset.UtcNow))
        {
            return Forbid();
        }

        Stream? stream;
        try
        {
            stream = await mediaStore.OpenReadAsync(mediaRef, ct);
        }
        catch (ArgumentException)
        {
            return NotFound();
        }

        return stream is null ? NotFound() : File(stream, mediaStore.GetContentType(mediaRef));
    }
}
