using First10.Application.Dispatch;
using First10.Domain.Incidents;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wolverine;

namespace First10.Api.Controllers;

public sealed record OutcomeRequest(TicketOutcome Outcome, string? Note);
public sealed record OverrideRequest(string? Reason);
public sealed record SeverityRequest(SeverityTier Severity);

/// <summary>
/// The dispatcher's hands (M3). Every endpoint publishes an explicit-action command —
/// the ONLY origin of loop-closure messages (R1e), enforced by the outbox: no
/// committed action, no reporter notification. The acting officer is the AUTHENTICATED
/// principal, never client-supplied — the OIDC token (DevAuth's "dev-console" locally)
/// is what the audit trail records, so it cannot be spoofed from the request body.
/// </summary>
[ApiController]
[Authorize(Policy = "Dispatcher")]
[Route("api/tickets/{id:guid}/actions")]
public class DispatcherActionsController(IMessageBus bus) : ControllerBase
{
    [HttpPost("dispatch")]
    public Task<IActionResult> Dispatch(Guid id) =>
        Publish(new DispatcherAction(id, DispatcherActionKind.Dispatched, Officer()));

    [HttpPost("arrive")]
    public Task<IActionResult> Arrive(Guid id) =>
        Publish(new DispatcherAction(id, DispatcherActionKind.Arrived, Officer()));

    [HttpPost("transport")]
    public Task<IActionResult> Transport(Guid id) =>
        Publish(new DispatcherAction(id, DispatcherActionKind.Transported, Officer()));

    [HttpPost("outcome")]
    public Task<IActionResult> Outcome(Guid id, [FromBody] OutcomeRequest request) =>
        Publish(new MarkOutcome(id, request.Outcome, Officer(), request.Note));

    /// <summary>One-click severity re-grade (audited like every action; no-op if unchanged).</summary>
    [HttpPost("severity")]
    public Task<IActionResult> Severity(Guid id, [FromBody] SeverityRequest request) =>
        !Enum.IsDefined(request.Severity)
            ? Task.FromResult<IActionResult>(BadRequest($"Unknown severity {(int)request.Severity}."))
            : Publish(new RegradeSeverity(id, request.Severity, Officer()));

    /// <summary>Manual triage override — dispatcher is the final gate (D-008). Reason is mandatory.</summary>
    [HttpPost("promote")]
    public Task<IActionResult> Promote(Guid id, [FromBody] OverrideRequest request) =>
        Override(id, OverrideKind.Promote, request);

    [HttpPost("reject")]
    public Task<IActionResult> Reject(Guid id, [FromBody] OverrideRequest request) =>
        Override(id, OverrideKind.Reject, request);

    private Task<IActionResult> Override(Guid id, OverrideKind kind, OverrideRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return Task.FromResult<IActionResult>(BadRequest("A reason is required for a triage override."));
        }
        return Publish(new OverrideDisposition(id, kind, Officer(), request.Reason.Trim()));
    }

    private async Task<IActionResult> Publish(object command)
    {
        await bus.PublishAsync(command);
        return Accepted();
    }

    private string Officer() =>
        User.Identity?.Name is { Length: > 0 } name ? name : "unidentified-dispatcher";
}
