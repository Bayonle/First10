using First10.Application.Ingest;
using First10.Application.Triage;
using First10.Domain.Abstractions;
using First10.Domain.Triage;
using First10.Infrastructure.Ai;
using First10.Infrastructure.Media;
using First10.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using OpenAI;
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

// ---- Triage services (M1) ----
builder.Services.Configure<TriageOptions>(builder.Configuration.GetSection("Triage"));
builder.Services.AddSingleton(sp =>
    sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<TriageOptions>>().Value);

// Media store (D-012/D-016): S3-compatible (MinIO in dev via Aspire) when configured,
// filesystem fallback for bare `dotnet run` / tests.
var s3ServiceUrl = builder.Configuration["Media:S3:ServiceUrl"];
if (!string.IsNullOrWhiteSpace(s3ServiceUrl))
{
    var s3 = new Amazon.S3.AmazonS3Client(
        new Amazon.Runtime.BasicAWSCredentials(
            builder.Configuration["Media:S3:AccessKey"] ?? "first10",
            builder.Configuration["Media:S3:SecretKey"] ?? "first10dev"),
        new Amazon.S3.AmazonS3Config
        {
            ServiceURL = s3ServiceUrl,
            ForcePathStyle = true, // MinIO requires path-style addressing
        });
    builder.Services.AddSingleton<IMediaStore>(
        new S3MediaStore(s3, builder.Configuration["Media:S3:Bucket"] ?? "first10-media"));
}
else
{
    var mediaRoot = builder.Configuration["Media:RootPath"]
        ?? Path.Combine(builder.Environment.ContentRootPath, "data", "media");
    builder.Services.AddSingleton<IMediaStore>(new FileSystemMediaStore(mediaRoot));
}
builder.Services.AddSingleton<IPerceptualHasher, DHashPerceptualHasher>();

// Stage 1 classifier: LLM behind IChatClient when a key is configured (D-003),
// heuristic fallback otherwise so the funnel runs offline (dev/CI/tests).
var openAiKey = builder.Configuration["OpenAI:ApiKey"];
if (!string.IsNullOrWhiteSpace(openAiKey))
{
    var model = builder.Configuration["OpenAI:Model"] ?? "gpt-4o-mini";
    builder.Services.AddSingleton<IChatClient>(
        new OpenAIClient(openAiKey).GetChatClient(model).AsIChatClient());
    builder.Services.AddSingleton<IIntentClassifier, ChatIntentClassifier>();
}
else
{
    builder.Services.AddSingleton<IIntentClassifier, HeuristicIntentClassifier>();
}

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
