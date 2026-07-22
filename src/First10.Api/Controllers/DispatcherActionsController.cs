using First10.Application.Dispatch;
using First10.Domain.Incidents;
using Microsoft.AspNetCore.Mvc;
using Wolverine;

namespace First10.Api.Controllers;

public sealed record ActionRequest(string? Officer);
public sealed record OutcomeRequest(TicketOutcome Outcome, string? Officer, string? Note);

/// <summary>
/// The dispatcher's hands (M3). Every endpoint publishes an explicit-action command —
/// the ONLY origin of loop-closure messages (R1e), enforced by the outbox: no
/// committed action, no reporter notification. M4 adds OIDC + real officer identity.
/// </summary>
[ApiController]
[Route("api/tickets/{id:guid}/actions")]
public class DispatcherActionsController(IMessageBus bus) : ControllerBase
{
    [HttpPost("dispatch")]
    public Task<IActionResult> Dispatch(Guid id, [FromBody] ActionRequest request) =>
        Publish(new DispatcherAction(id, DispatcherActionKind.Dispatched, Officer(request)));

    [HttpPost("arrive")]
    public Task<IActionResult> Arrive(Guid id, [FromBody] ActionRequest request) =>
        Publish(new DispatcherAction(id, DispatcherActionKind.Arrived, Officer(request)));

    [HttpPost("transport")]
    public Task<IActionResult> Transport(Guid id, [FromBody] ActionRequest request) =>
        Publish(new DispatcherAction(id, DispatcherActionKind.Transported, Officer(request)));

    [HttpPost("outcome")]
    public Task<IActionResult> Outcome(Guid id, [FromBody] OutcomeRequest request) =>
        Publish(new MarkOutcome(id, request.Outcome, request.Officer is { Length: > 0 } o ? o : "duty-officer", request.Note));

    private async Task<IActionResult> Publish(object command)
    {
        await bus.PublishAsync(command);
        return Accepted();
    }

    private static string Officer(ActionRequest request) =>
        request.Officer is { Length: > 0 } o ? o : "duty-officer";
}
