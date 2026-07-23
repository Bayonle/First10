using First10.Api.Controllers;
using First10.Domain.Incidents;
using First10.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace First10.Tests;

/// <summary>
/// Console KPIs are whole-table server aggregates — the numbers a dispatcher steers by
/// must not inherit the queue endpoint's 100-row cap (which would clip during floods).
/// </summary>
public class KpiEndpointTests
{
    private static First10DbContext NewDb() =>
        new(new DbContextOptionsBuilder<First10DbContext>()
            .UseInMemoryDatabase($"first10-{Guid.NewGuid()}")
            .Options);

    private static IncidentTicket Ticket(TicketStatus status, SeverityTier? sev = null,
        DispatchState dispatch = DispatchState.None, int ageMinutes = 0) => new()
    {
        Id = Guid.NewGuid(),
        Status = status,
        Severity = sev,
        Dispatch = dispatch,
        Summary = "t",
        CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-ageMinutes),
        UpdatedAt = DateTimeOffset.UtcNow,
    };

    [Fact]
    public async Task Kpis_aggregate_the_whole_table_beyond_the_list_cap()
    {
        await using var db = NewDb();
        // 130 open tickets — more than the queue's 100-row page.
        for (var i = 0; i < 130; i++)
        {
            db.Tickets.Add(Ticket(TicketStatus.Provisional, i < 40 ? SeverityTier.High : SeverityTier.Low,
                ageMinutes: i == 0 ? 90 : 5));
        }
        db.Tickets.Add(Ticket(TicketStatus.Promoted, SeverityTier.High, DispatchState.Dispatched));
        db.Tickets.Add(Ticket(TicketStatus.Closed, SeverityTier.High)); // closed — excluded
        db.Tickets.Add(Ticket(TicketStatus.Rejected)); // rejected — excluded
        await db.SaveChangesAsync();

        var result = JsonSerializer.Serialize(await new TicketsController(db).Kpis(default));
        using var kpis = JsonDocument.Parse(result);

        Assert.Equal(131, kpis.RootElement.GetProperty("active").GetInt32()); // 130 + dispatched one
        Assert.Equal(41, kpis.RootElement.GetProperty("highSev").GetInt32()); // 40 + dispatched one
        Assert.Equal(130, kpis.RootElement.GetProperty("unassigned").GetInt32());
        Assert.InRange(kpis.RootElement.GetProperty("oldestWaitMinutes").GetDouble(), 89, 92);
    }

    [Fact]
    public async Task Empty_table_yields_zeroes_not_errors()
    {
        await using var db = NewDb();
        var result = JsonSerializer.Serialize(await new TicketsController(db).Kpis(default));
        using var kpis = JsonDocument.Parse(result);

        Assert.Equal(0, kpis.RootElement.GetProperty("active").GetInt32());
        Assert.Equal(0, kpis.RootElement.GetProperty("oldestWaitMinutes").GetDouble());
    }
}
