using First10.Api.Hubs;
using First10.Application.Ingest;
using Microsoft.AspNetCore.SignalR;

namespace First10.Api.Notifications;

/// <summary>
/// Bridges outbox-cascaded domain events to SignalR. Runs after the ingest transaction
/// committed, so the console never hears about state that didn't stick.
/// </summary>
public static class TicketUpsertedHandler
{
    public static Task Handle(TicketUpserted @event, IHubContext<ConsoleHub> hub, CancellationToken ct) =>
        hub.Clients.All.SendAsync("ticketChanged", @event.TicketId, ct);
}
