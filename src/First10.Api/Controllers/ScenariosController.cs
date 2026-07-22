using First10.Api.Filters;
using First10.Domain.Channels;
using Microsoft.AspNetCore.Mvc;
using Wolverine;

namespace First10.Api.Controllers;

/// <summary>
/// Scenario runner v1 (D-006): scripted envelope sequences through the real pipeline.
/// These become the repeatable §7.4 test protocols in M5. Dev-only.
/// </summary>
[ApiController]
[Route("api/local-chat/scenarios")]
[DevelopmentOnly]
public class ScenariosController(IMessageBus bus) : ControllerBase
{
    public sealed record ScenarioStep(string Sender, InboundKind Kind, string? Text, GeoPoint? Location, double DelaySeconds);

    private static readonly Dictionary<string, ScenarioStep[]> Catalog = new(StringComparer.OrdinalIgnoreCase)
    {
        // Text-only report → REVIEW + challenge; reporter answers with a pin.
        ["text-only-challenge"] =
        [
            new("scn-tunde", InboundKind.Text, "There has been an accident near Ibafo, a car ran into the bush", null, 0),
            new("scn-tunde", InboundKind.LocationPin, null, new GeoPoint(6.7430, 3.4300), 6),
        ],
        // Two reporters, same stretch, minutes apart. (Merges into one incident from M2;
        // in M1 this demonstrates two independent tickets for the same crash.)
        ["two-reporters"] =
        [
            new("scn-amaka", InboundKind.Text, "Trailer don fall for Kara bridge! People dey trapped o", null, 0),
            new("scn-amaka", InboundKind.LocationPin, null, new GeoPoint(6.6650, 3.3830), 4),
            new("scn-bello", InboundKind.Text, "Accident at Kara bridge inward Lagos, trailer overturned", null, 8),
        ],
        // R11: distinct numbers flood the line → flood banner + REVIEW caps.
        ["spam-flood"] = Enumerable.Range(1, 10)
            .Select(i => new ScenarioStep($"scn-flood-{i}", InboundKind.Text, $"accident!! come quick {i}", null, i * 1.5))
            .ToArray(),
        // Off-corridor pin → flagged, never dropped.
        ["outside-corridor"] =
        [
            new("scn-far", InboundKind.Text, "Bad crash on the road here, two cars", null, 0),
            new("scn-far", InboundKind.LocationPin, null, new GeoPoint(9.0765, 7.3986), 4), // Abuja
        ],
        // Non-incidents → canned replies, no tickets.
        ["non-incidents"] =
        [
            new("scn-ada", InboundKind.Text, "Good morning", null, 0),
            new("scn-ada", InboundKind.Text, "How far, which number be this?", null, 3),
        ],
    };

    [HttpGet]
    public IReadOnlyList<string> List() => [.. Catalog.Keys];

    [HttpPost("{name}")]
    public async Task<IActionResult> Run(string name)
    {
        if (!Catalog.TryGetValue(name, out var steps))
        {
            return NotFound($"Unknown scenario '{name}'. Available: {string.Join(", ", Catalog.Keys)}");
        }

        var startedAt = DateTimeOffset.UtcNow;
        foreach (var step in steps)
        {
            var envelope = new InboundChannelMessage(
                ChannelKind.Local,
                step.Sender,
                Guid.NewGuid().ToString("N"),
                step.Kind,
                step.Text,
                MediaRef: null,
                step.Location,
                startedAt.AddSeconds(step.DelaySeconds));

            if (step.DelaySeconds <= 0)
            {
                await bus.PublishAsync(envelope);
            }
            else
            {
                // Durable scheduled delivery — survives restarts, arrives in order.
                await bus.ScheduleAsync(envelope, TimeSpan.FromSeconds(step.DelaySeconds));
            }
        }

        return Accepted(new { scenario = name, steps = steps.Length });
    }
}
