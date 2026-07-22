namespace First10.Application.Ingest;

/// <summary>Raised whenever a ticket is created or its timeline changes. Drives console push.</summary>
public sealed record TicketUpserted(Guid TicketId);
