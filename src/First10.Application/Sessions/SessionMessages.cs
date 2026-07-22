using Wolverine.Persistence.Sagas;

namespace First10.Application.Sessions;

/// <summary>Cascaded by ingest when a ticket opens — starts the session saga.</summary>
public sealed record SessionOpened(
    [property: SagaIdentity] Guid TicketId,
    bool PinAskPending,
    bool ChallengePending);

/// <summary>Paper R5c: fires PinReminderSeconds after a pin was requested.</summary>
public sealed record PinReminderDue([property: SagaIdentity] Guid TicketId);

/// <summary>Unanswered challenge (no evidence, no location) expires the ticket.</summary>
public sealed record ChallengeExpiryDue([property: SagaIdentity] Guid TicketId);

/// <summary>Proactive session-age cap — replaces the lazy check for the no-next-message case.</summary>
public sealed record SessionAgeCapDue([property: SagaIdentity] Guid TicketId);

/// <summary>Cascaded when a ticket reaches a terminal state elsewhere (merge, lazy boundary, dispatcher).</summary>
public sealed record SessionEnded([property: SagaIdentity] Guid TicketId);
