namespace First10.Domain.Channels;

public enum InboundKind
{
    Text = 0,
    Image = 1,
    Voice = 2,
    LocationPin = 3,
}

/// <summary>
/// The normalized envelope every channel adapter produces (D-005). By the time this
/// exists, channel dirty work is done: media downloaded, images blurred, audio transcoded.
/// The core pipeline never sees anything channel-specific beyond <see cref="Channel"/>.
/// </summary>
public sealed record InboundChannelMessage(
    ChannelKind Channel,
    string ExternalUserId,
    string ExternalMessageId,
    InboundKind Kind,
    string? Text,
    string? MediaRef,
    GeoPoint? Location,
    DateTimeOffset OccurredAt);

public readonly record struct GeoPoint(double Latitude, double Longitude);
