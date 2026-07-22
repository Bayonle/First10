using First10.Domain.Triage;
using First10.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace First10.Api.Controllers;

public sealed record FloodState(bool Active, int TicketsInWindow, int Threshold, int WindowMinutes);

[ApiController]
[Route("api/system")]
public class SystemController(First10DbContext db, TriageOptions options) : ControllerBase
{
    /// <summary>R11 flood banner state for the console.</summary>
    [HttpGet("flood")]
    public async Task<FloodState> Flood(CancellationToken ct)
    {
        var windowStart = DateTimeOffset.UtcNow.AddMinutes(-options.FloodWindowMinutes);
        var count = await db.Tickets.CountAsync(t => t.CreatedAt >= windowStart, ct);
        return new FloodState(
            count >= options.FloodDistinctConversations,
            count,
            options.FloodDistinctConversations,
            options.FloodWindowMinutes);
    }
}
