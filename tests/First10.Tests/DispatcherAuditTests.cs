using System.Security.Claims;
using First10.Api.Controllers;
using Microsoft.EntityFrameworkCore;
using First10.Application.Dispatch;
using First10.Domain.Incidents;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Wolverine;

namespace First10.Tests;

/// <summary>
/// Dispatcher actions are attributable: the recorded officer is the AUTHENTICATED
/// principal (never client-supplied), and every action writes a queryable
/// AccessKind.DispatcherAction row in the same transaction as the state change.
/// </summary>
public class DispatcherAuditTests
{
    private static DispatcherActionsController Controller(IMessageBus bus, string? name)
    {
        var identity = name is null
            ? new ClaimsIdentity() // unauthenticated shape — cannot normally reach the endpoint
            : new ClaimsIdentity([new Claim(ClaimTypes.Name, name)], "TestAuth");
        return new DispatcherActionsController(bus)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) },
            },
        };
    }

    [Fact]
    public async Task Officer_identity_comes_from_the_authenticated_principal()
    {
        var bus = Substitute.For<IMessageBus>();
        await Controller(bus, "officer-adeyemi").Dispatch(Guid.NewGuid());

        var command = bus.ReceivedCalls().SelectMany(c => c.GetArguments()).OfType<DispatcherAction>().Single();
        Assert.Equal("officer-adeyemi", command.Officer);
    }

    [Fact]
    public async Task Outcome_marking_uses_the_principal_too_and_ignores_any_client_name()
    {
        var bus = Substitute.For<IMessageBus>();
        await Controller(bus, "officer-bello").Outcome(Guid.NewGuid(), new OutcomeRequest(TicketOutcome.False, "prank call"));

        var command = bus.ReceivedCalls().SelectMany(c => c.GetArguments()).OfType<MarkOutcome>().Single();
        Assert.Equal("officer-bello", command.Officer);
        Assert.Equal("prank call", command.Note);
    }

    [Fact]
    public async Task Nameless_principal_is_recorded_as_unidentified_not_blank()
    {
        var bus = Substitute.For<IMessageBus>();
        await Controller(bus, null).Dispatch(Guid.NewGuid());

        var command = bus.ReceivedCalls().SelectMany(c => c.GetArguments()).OfType<DispatcherAction>().Single();
        Assert.Equal("unidentified-dispatcher", command.Officer);
    }

    // ---- Handler side: audit rows in the action's own transaction ----

    private static async Task<(First10.Infrastructure.Persistence.First10DbContext Db, IncidentTicket Ticket)> TicketWithReporter()
    {
        var db = new First10.Infrastructure.Persistence.First10DbContext(
            new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<First10.Infrastructure.Persistence.First10DbContext>()
                .UseInMemoryDatabase($"first10-{Guid.NewGuid()}")
                .Options);
        var conversation = new First10.Domain.Conversations.Conversation
        {
            Id = Guid.NewGuid(),
            Channel = First10.Domain.Channels.ChannelKind.Local,
            ExternalUserId = "rep-1",
            CreatedAt = DateTimeOffset.UtcNow,
            LastInboundAt = DateTimeOffset.UtcNow,
        };
        var ticket = new IncidentTicket
        {
            Id = Guid.NewGuid(),
            Status = TicketStatus.Promoted,
            Summary = "test incident",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        db.Conversations.Add(conversation);
        db.Tickets.Add(ticket);
        db.TimelineEntries.Add(new TimelineEntry
        {
            Id = Guid.NewGuid(),
            TicketId = ticket.Id,
            ConversationId = conversation.Id,
            Direction = TimelineDirection.Inbound,
            Kind = TimelineEntryKind.Text,
            Text = "accident o",
            OccurredAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
        return (db, ticket);
    }

    [Fact]
    public async Task Every_action_writes_a_queryable_audit_row_with_the_officer()
    {
        var (db, ticket) = await TicketWithReporter();
        await using var _ = db;

        foreach (var kind in new[] { DispatcherActionKind.Dispatched, DispatcherActionKind.Arrived, DispatcherActionKind.Transported })
        {
            await DispatcherActionHandler.Handle(
                new DispatcherAction(ticket.Id, kind, "officer-chidi"),
                db, new First10.Application.Triage.HeuristicTimelineSummarizer(),
                Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance, CancellationToken.None);
            await db.SaveChangesAsync();
        }

        var audits = db.AccessLogs.Where(a => a.Kind == AccessKind.DispatcherAction).ToList();
        Assert.Equal(["arrived", "dispatched", "transported"], audits.Select(a => a.Detail).Order());
        Assert.All(audits, a =>
        {
            Assert.Equal("officer-chidi", a.Who);
            Assert.Equal(ticket.Id, a.TicketId);
        });
    }

    [Fact]
    public async Task Outcome_marking_is_audited_with_the_verdict()
    {
        var (db, ticket) = await TicketWithReporter();
        await using var _ = db;

        await DispatcherActionHandler.Handle(
            new MarkOutcome(ticket.Id, TicketOutcome.False, "officer-chidi", "prank"),
            db, new First10.Domain.Triage.TriageOptions(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance, CancellationToken.None);
        await db.SaveChangesAsync();

        var audit = db.AccessLogs.Single(a => a.Kind == AccessKind.DispatcherAction);
        Assert.Equal("outcome:False", audit.Detail);
        Assert.Equal("officer-chidi", audit.Who);
    }

    [Fact]
    public async Task Severity_regrade_is_audited_and_noted_and_noops_when_unchanged()
    {
        var (db, ticket) = await TicketWithReporter();
        await using var _ = db;
        ticket.Severity = SeverityTier.High;
        await db.SaveChangesAsync();

        await DispatcherActionHandler.Handle(
            new RegradeSeverity(ticket.Id, SeverityTier.Medium, "officer-chidi"), db, CancellationToken.None);
        await db.SaveChangesAsync();

        Assert.Equal(SeverityTier.Medium, ticket.Severity);
        Assert.Equal("severity:Medium", db.AccessLogs.Single(a => a.Kind == AccessKind.DispatcherAction).Detail);
        Assert.Contains(db.TimelineEntries.Where(e => e.Direction == TimelineDirection.System).ToList(),
            e => e.Text!.Contains("High → Medium"));

        // Same grade again — nothing changed, nothing audited.
        await DispatcherActionHandler.Handle(
            new RegradeSeverity(ticket.Id, SeverityTier.Medium, "officer-chidi"), db, CancellationToken.None);
        await db.SaveChangesAsync();
        Assert.Single(db.AccessLogs.Where(a => a.Kind == AccessKind.DispatcherAction));
    }

    // ---- Manual disposition override (dispatcher is the final gate, D-008) ----

    [Fact]
    public async Task Promote_override_lifts_a_provisional_ticket_with_audited_reason()
    {
        var (db, ticket) = await TicketWithReporter();
        await using var _ = db;
        ticket.Status = TicketStatus.Provisional;
        await db.SaveChangesAsync();

        await DispatcherActionHandler.Handle(
            new OverrideDisposition(ticket.Id, OverrideKind.Promote, "officer-chidi", "caller is a trained bystander, credible detail"),
            db, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance, CancellationToken.None);
        await db.SaveChangesAsync();

        Assert.Equal(TicketStatus.Promoted, ticket.Status);
        var audit = db.AccessLogs.Single(a => a.Kind == AccessKind.DispatcherAction);
        Assert.Equal("override:promote", audit.Detail);
        var note = db.TimelineEntries.Single(e => e.Direction == TimelineDirection.System && e.Text!.Contains("PROMOTED"));
        Assert.Contains("trained bystander", note.Text);
    }

    [Fact]
    public async Task Reject_override_frees_the_conversation_ends_the_session_and_skips_reputation()
    {
        var (db, ticket) = await TicketWithReporter();
        await using var _ = db;
        var conversation = db.Conversations.Single();
        conversation.ActiveTicketId = ticket.Id;
        await db.SaveChangesAsync();

        var outgoing = await DispatcherActionHandler.Handle(
            new OverrideDisposition(ticket.Id, OverrideKind.Reject, "officer-chidi", "duplicate of earlier incident"),
            db, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance, CancellationToken.None);
        await db.SaveChangesAsync();

        Assert.Equal(TicketStatus.Rejected, ticket.Status);
        Assert.Null(conversation.ActiveTicketId); // reporter can report new incidents
        Assert.Contains(outgoing, m => m is First10.Application.Sessions.SessionEnded); // saga timers die
        Assert.Empty(db.ReporterReputations); // override-reject is "not actionable", NOT a false-report strike
        Assert.Equal("override:reject", db.AccessLogs.Single(a => a.Kind == AccessKind.DispatcherAction).Detail);
    }

    [Fact]
    public async Task Dispatched_tickets_cannot_be_reject_overridden()
    {
        var (db, ticket) = await TicketWithReporter();
        await using var _ = db;
        ticket.Dispatch = DispatchState.Dispatched;
        await db.SaveChangesAsync();

        await DispatcherActionHandler.Handle(
            new OverrideDisposition(ticket.Id, OverrideKind.Reject, "officer-chidi", "changed my mind"),
            db, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance, CancellationToken.None);
        await db.SaveChangesAsync();

        Assert.NotEqual(TicketStatus.Rejected, ticket.Status); // crews are moving — mark the OUTCOME instead
        Assert.Empty(db.AccessLogs.Where(a => a.Kind == AccessKind.DispatcherAction));
    }

    [Fact]
    public async Task Override_endpoints_refuse_a_missing_reason()
    {
        var bus = Substitute.For<IMessageBus>();
        var controller = Controller(bus, "officer-adeyemi");

        var result = await controller.Promote(Guid.NewGuid(), new OverrideRequest("  "));

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Empty(bus.ReceivedCalls()); // nothing published without a reason
    }

    [Fact]
    public async Task Override_commands_carry_the_principal_and_trimmed_reason()
    {
        var bus = Substitute.For<IMessageBus>();
        await Controller(bus, "officer-adeyemi").Reject(Guid.NewGuid(), new OverrideRequest("  test ticket  "));

        var command = bus.ReceivedCalls().SelectMany(c => c.GetArguments()).OfType<OverrideDisposition>().Single();
        Assert.Equal("officer-adeyemi", command.Officer);
        Assert.Equal("test ticket", command.Reason);
        Assert.Equal(OverrideKind.Reject, command.Kind);
    }

    [Fact]
    public async Task Invalid_transitions_write_no_audit_row()
    {
        var (db, ticket) = await TicketWithReporter();
        await using var _ = db;

        // Arrive without dispatch — ignored, so nothing to audit.
        await DispatcherActionHandler.Handle(
            new DispatcherAction(ticket.Id, DispatcherActionKind.Arrived, "officer-chidi"),
            db, new First10.Application.Triage.HeuristicTimelineSummarizer(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance, CancellationToken.None);
        await db.SaveChangesAsync();

        Assert.Empty(db.AccessLogs.Where(a => a.Kind == AccessKind.DispatcherAction));
    }
}
