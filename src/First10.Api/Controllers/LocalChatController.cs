using First10.Api.Filters;
using First10.Domain.Channels;
using Microsoft.AspNetCore.Mvc;
using Wolverine;

namespace First10.Api.Controllers;

public sealed record LocalChatMessageRequest(
    string SenderId,
    string? Text,
    string? MediaRef,
    double? Latitude,
    double? Longitude,
    LocalMessageKind Kind = LocalMessageKind.Text);

public enum LocalMessageKind
{
    Text = 0,
    Image = 1,
    Voice = 2,
    LocationPin = 3,
}

/// <summary>
/// The Local channel adapter (D-006): the dev cockpit posts here, and messages enter
/// the exact same pipeline as WhatsApp/Telegram — normalized envelope, durable queue,
/// same handlers. Nothing downstream can tell it's fake.
/// </summary>
[ApiController]
[Route("api/local-chat")]
[DevelopmentOnly] // D-006 hard gate — see DevelopmentOnlyAttribute
public class LocalChatController(IMessageBus bus) : ControllerBase
{
    [HttpPost("messages")]
    public async Task<IActionResult> Send([FromBody] LocalChatMessageRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.SenderId))
        {
            return BadRequest("SenderId is required.");
        }

        if (request.Kind == LocalMessageKind.Text && string.IsNullOrWhiteSpace(request.Text))
        {
            return BadRequest("Text is required for text messages.");
        }

        var envelope = new InboundChannelMessage(
            Channel: ChannelKind.Local,
            ExternalUserId: request.SenderId.Trim(),
            ExternalMessageId: Guid.NewGuid().ToString("N"), // local provider mints its own ids
            Kind: (InboundKind)request.Kind,
            Text: request.Text,
            MediaRef: request.MediaRef,
            Location: request is { Latitude: { } lat, Longitude: { } lng } ? new GeoPoint(lat, lng) : null,
            OccurredAt: DateTimeOffset.UtcNow);

        // Same contract as every real adapter: publish and return fast.
        await bus.PublishAsync(envelope);
        return Accepted();
    }
}
