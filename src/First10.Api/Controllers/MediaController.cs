using First10.Domain.Abstractions;
using Microsoft.AspNetCore.Mvc;

namespace First10.Api.Controllers;

/// <summary>
/// Serves stored media to the console (images inline, audio for the player).
/// M4 replaces direct serving with short-lived signed URLs + access logging (D-012);
/// until then this is dev-scale plumbing behind the same API.
/// </summary>
[ApiController]
[Route("api/media")]
public class MediaController(IMediaStore mediaStore) : ControllerBase
{
    [HttpGet("{mediaRef}")]
    public async Task<IActionResult> Get(string mediaRef, CancellationToken ct)
    {
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
