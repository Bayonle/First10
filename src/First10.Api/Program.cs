using First10.Application.Ingest;
using First10.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.Postgresql;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Postgres")
    ?? throw new InvalidOperationException("ConnectionStrings:Postgres is not configured.");

// EF Core with Wolverine outbox integration (D-002): ticket-state changes,
// timeline appends, and cascaded messages commit in one transaction.
builder.Services.AddDbContextWithWolverineIntegration<First10DbContext>(
    x => x.UseNpgsql(connectionString));

builder.Host.UseWolverine(opts =>
{
    opts.PersistMessagesWithPostgresql(connectionString);
    opts.UseEntityFrameworkCoreTransactions();
    opts.Policies.AutoApplyTransactions();
    opts.Policies.UseDurableLocalQueues();

    // Handlers live in First10.Application, not this assembly.
    opts.Discovery.IncludeAssembly(typeof(IngestInboundMessageHandler).Assembly);
});

builder.Services.AddControllers();
builder.Services.AddSignalR();

const string SpaCors = "spa";
builder.Services.AddCors(options => options.AddPolicy(SpaCors, policy => policy
    .WithOrigins(builder.Configuration.GetSection("Cors:Origins").Get<string[]>()
        ?? ["http://localhost:5173"])
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials())); // SignalR requires credentials

var app = builder.Build();

// Dev/pilot convenience: apply schema on startup. Revisit for M4 (migration discipline).
if (!app.Environment.IsEnvironment("Testing"))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<First10DbContext>();
    await db.Database.MigrateAsync();
}

app.UseCors(SpaCors);
app.MapControllers();
app.MapHub<First10.Api.Hubs.ConsoleHub>("/hubs/console");

app.Run();

// Exposed for WebApplicationFactory-based tests.
public partial class Program;
