using First10.Application.Ingest;
using First10.Application.Outbound;
using First10.Application.Sessions;
using First10.Application.Triage;
using First10.Domain.Abstractions;
using First10.Domain.Incidents;
using First10.Domain.Triage;
using First10.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace First10.Tests;

public class SessionSagaTests
{
    private static First10DbContext NewDb() =>
        new(new DbContextOptionsBuilder<First10DbContext>()
            .UseInMemoryDatabase($"first10-{Guid.NewGuid()}")
            .Options);

    private static async Task<(IncidentTicket, Guid)> SeedTicket(
        First10DbContext db, EvidenceLevel evidence = EvidenceLevel.TextOnly, bool located = false)
    {
        var conversationId = Guid.NewGuid();
        var ticket = new IncidentTicket
        {
            Id = Guid.NewGuid(),
            Status = TicketStatus.Provisional,
            Summary = "test",
            Disposition = Disposition.Challenge,
            Evidence = evidence,
            Language = "english",
            ChallengeSentAt = DateTimeOffset.UtcNow,
            LocationResolvedAt = located ? DateTimeOffset.UtcNow : null,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        db.Tickets.Add(ticket);
        db.TimelineEntries.Add(new TimelineEntry
        {
            Id = Guid.NewGuid(),
            TicketId = ticket.Id,
            ConversationId = conversationId,
            Direction = TimelineDirection.Inbound,
            Kind = TimelineEntryKind.Text,
            Text = "accident",
            OccurredAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
        return (ticket, conversationId);
    }

    [Fact]
    public void Start_schedules_the_right_timers()
    {
        var (_, outgoing) = ReportingSessionSaga.Start(
            new SessionOpened(Guid.NewGuid(), PinAskPending: true, ChallengePending: true),
            new TriageOptions());
        Assert.Equal(3, outgoing.Count); // pin reminder + challenge expiry + age cap

        var (_, noPin) = ReportingSessionSaga.Start(
            new SessionOpened(Guid.NewGuid(), PinAskPending: false, ChallengePending: false),
            new TriageOptions());
        Assert.Single(noPin); // only the age cap
    }

    [Fact]
    public async Task Pin_reminder_fires_when_location_still_missing_and_noops_when_resolved()
    {
        await using var db = NewDb();
        var (ticket, _) = await SeedTicket(db);
        var saga = new ReportingSessionSaga { Id = ticket.Id };

        var outgoing = await saga.Handle(new PinReminderDue(ticket.Id), db, NullLogger.Instance, default);
        await db.SaveChangesAsync();
        Assert.Contains(outgoing, m => m is SendOutboundMessage); // reminder sent (R5c)

        // Resolved ticket → timer no-ops
        ticket.LocationResolvedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
        var second = await saga.Handle(new PinReminderDue(ticket.Id), db, NullLogger.Instance, default);
        Assert.Empty(second);
    }

    [Fact]
    public async Task Challenge_expiry_expires_unanswered_tickets_only()
    {
        await using var db = NewDb();
        var (unanswered, _) = await SeedTicket(db);
        var saga = new ReportingSessionSaga { Id = unanswered.Id };

        await saga.Handle(new ChallengeExpiryDue(unanswered.Id), db, NullLogger.Instance, default);
        await db.SaveChangesAsync();
        Assert.Equal(TicketStatus.ExpiredUnverified, unanswered.Status);

        // A located ticket must survive its challenge expiry timer
        await using var db2 = NewDb();
        var (located, _) = await SeedTicket(db2, located: true);
        await new ReportingSessionSaga { Id = located.Id }
            .Handle(new ChallengeExpiryDue(located.Id), db2, NullLogger.Instance, default);
        Assert.Equal(TicketStatus.Provisional, located.Status);
    }

    [Fact]
    public async Task Age_cap_closes_session_but_keeps_evidenced_ticket_pending()
    {
        await using var db = NewDb();
        var (located, _) = await SeedTicket(db, evidence: EvidenceLevel.Photo, located: true);
        var saga = new ReportingSessionSaga { Id = located.Id };

        await saga.Handle(new SessionAgeCapDue(located.Id), db, NullLogger.Instance, default);
        await db.SaveChangesAsync();

        Assert.Equal(TicketStatus.Provisional, located.Status); // pending dispatch, not expired
        Assert.Contains(db.TimelineEntries, e =>
            e.Direction == TimelineDirection.System && e.Text!.Contains("age cap"));
    }
}

public class ExtractionTests
{
    private static First10DbContext NewDb() =>
        new(new DbContextOptionsBuilder<First10DbContext>()
            .UseInMemoryDatabase($"first10-{Guid.NewGuid()}")
            .Options);

    private static async Task<(IncidentTicket, Guid)> SeedTicket(First10DbContext db, string text)
    {
        var conversationId = Guid.NewGuid();
        var ticket = new IncidentTicket
        {
            Id = Guid.NewGuid(),
            Status = TicketStatus.Provisional,
            Summary = text,
            Disposition = Disposition.Review,
            Language = "pidgin",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        db.Tickets.Add(ticket);
        db.TimelineEntries.Add(new TimelineEntry
        {
            Id = Guid.NewGuid(),
            TicketId = ticket.Id,
            ConversationId = conversationId,
            Direction = TimelineDirection.Inbound,
            Kind = TimelineEntryKind.Text,
            Text = text,
            OccurredAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
        return (ticket, conversationId);
    }

    private static Task<Wolverine.OutgoingMessages> Run(
        First10DbContext db, IncidentTicket ticket, Guid conversationId, TriageOptions options) =>
        RunExtractionHandler.Handle(
            new RunExtraction(ticket.Id, conversationId), db,
            new HeuristicIncidentExtractor(), new NullMediaStore(), options,
            NullLogger.Instance, CancellationToken.None);

    private static void SeedTemplate(First10DbContext db, bool approved) =>
        db.MicroInstructionTemplates.Add(new MicroInstructionTemplate
        {
            Id = Guid.NewGuid(),
            Key = "rta_fire",
            Language = "pidgin",
            Text = "DANGER: comot far from the motor...",
            ApprovedBy = approved ? "Dr. Test" : null,
            ApprovedAt = approved ? DateTimeOffset.UtcNow : null,
        });

    [Fact]
    public async Task Extraction_sets_severity_and_errs_high_on_fire()
    {
        await using var db = NewDb();
        var (ticket, conv) = await SeedTicket(db, "trailer don catch fire, person trapped inside");
        SeedTemplate(db, approved: true);
        await db.SaveChangesAsync();

        var outgoing = await Run(db, ticket, conv, new TriageOptions());
        await db.SaveChangesAsync();

        Assert.Equal(SeverityTier.High, ticket.Severity);
        Assert.NotNull(ticket.InstructionSentAt);
        Assert.Contains(outgoing, m => m is SendOutboundMessage { Kind: OutboundKind.MicroInstruction });
    }

    [Fact]
    public async Task Unapproved_template_is_never_sent_in_pilot_config()
    {
        await using var db = NewDb();
        var (ticket, conv) = await SeedTicket(db, "fire for the tanker o");
        SeedTemplate(db, approved: false);
        await db.SaveChangesAsync();

        // Pilot config: AllowUnapprovedTemplates=false (the default) — G3 clinical gate.
        var outgoing = await Run(db, ticket, conv, new TriageOptions());
        await db.SaveChangesAsync();

        Assert.Null(ticket.InstructionSentAt);
        Assert.DoesNotContain(outgoing, m => m is SendOutboundMessage { Kind: OutboundKind.MicroInstruction });

        // Dev config: explicitly allowed → sends, marked unapproved in the audit note.
        var devOutgoing = await Run(db, ticket, conv, new TriageOptions { AllowUnapprovedTemplates = true });
        Assert.Contains(devOutgoing, m => m is SendOutboundMessage { Kind: OutboundKind.MicroInstruction });
    }

    [Fact]
    public async Task Instruction_sent_once_per_ticket()
    {
        await using var db = NewDb();
        var (ticket, conv) = await SeedTicket(db, "okada accident, person dey bleed");
        SeedTemplate(db, approved: true);
        db.MicroInstructionTemplates.Add(new MicroInstructionTemplate
        {
            Id = Guid.NewGuid(), Key = "rta_okada", Language = "pidgin",
            Text = "No remove helmet...", ApprovedBy = "Dr. Test", ApprovedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        var first = await Run(db, ticket, conv, new TriageOptions());
        await db.SaveChangesAsync();
        var second = await Run(db, ticket, conv, new TriageOptions());

        Assert.Contains(first, m => m is SendOutboundMessage { Kind: OutboundKind.MicroInstruction });
        Assert.DoesNotContain(second, m => m is SendOutboundMessage { Kind: OutboundKind.MicroInstruction });
    }
}
