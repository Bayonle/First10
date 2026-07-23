using First10.Application.Dispatch;
using First10.Domain.Incidents;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wolverine;

namespace First10.Api.Controllers;

public sealed record OutcomeRequest(TicketOutcome Outcome, string? Note);

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

    private async Task<IActionResult> Publish(object command)
    {
        await bus.PublishAsync(command);
        return Accepted();
    }

    private string Officer() =>
        User.Identity?.Name is { Length: > 0 } name ? name : "unidentified-dispatcher";
}
