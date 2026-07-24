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
using Wolverine.ErrorHandling;
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

    // Transient DB failures (unique-index races on concurrent conversation creation,
    // connection blips, crash-replay artifacts) get retries before dead-lettering —
    // a dead-lettered inbound is a silently ignored crash report. The ladder must
    // OUTLAST the race window: a new reporter's rapid messages collide on conversation
    // creation while the first message's transaction is held open by a multi-second
    // LLM classification call — the M4 load test dead-lettered 96/300 messages on a
    // 2.6s ladder. Total cooldown here ≈ 21s, past any sane LLM p99.
    TimeSpan[] transientRetryLadder =
    [
        TimeSpan.FromMilliseconds(250), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3),
        TimeSpan.FromSeconds(7), TimeSpan.FromSeconds(10),
    ];
    opts.OnException<Microsoft.EntityFrameworkCore.DbUpdateException>()
        .RetryWithCooldown(transientRetryLadder);
    opts.OnException<Npgsql.NpgsqlException>()
        .RetryWithCooldown(transientRetryLadder);
    // Channel-send blips (Telegram/WhatsApp API hiccups) must not dead-letter a
    // loop-closure or instruction — retry with the same patience.
    opts.OnException<HttpRequestException>()
        .RetryWithCooldown(transientRetryLadder);

    // Handlers live in First10.Application, not this assembly.
    opts.Discovery.IncludeAssembly(typeof(IngestInboundMessageHandler).Assembly);
});

builder.Services.AddControllers();
builder.Services.AddSignalR();

// ---- AuthN/Z (M4): console endpoints require an authenticated dispatcher ----
// Real deployments: OIDC bearer tokens (Entra ID or equivalent) via Auth:Authority.
// Development/Testing without an authority: DevAuth authenticates everything as
// "dev-console" so authorization stays structurally enforced end-to-end.
var oidcAuthority = builder.Configuration["Auth:Authority"];
if (!string.IsNullOrWhiteSpace(oidcAuthority))
{
    builder.Services
        .AddAuthentication(Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(o =>
        {
            o.Authority = oidcAuthority;
            o.Audience = builder.Configuration["Auth:Audience"];
            o.Events = new Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerEvents
            {
                // SignalR WebSockets can't send Authorization headers; the token rides
                // the query string on hub paths only (standard SignalR bearer pattern).
                OnMessageReceived = ctx =>
                {
                    var accessToken = ctx.Request.Query["access_token"];
                    if (!string.IsNullOrEmpty(accessToken) && ctx.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                    {
                        ctx.Token = accessToken;
                    }
                    return Task.CompletedTask;
                },
            };
        });
}
else if (builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing"))
{
    builder.Services
        .AddAuthentication(First10.Api.Auth.DevAuthHandler.SchemeName)
        .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, First10.Api.Auth.DevAuthHandler>(
            First10.Api.Auth.DevAuthHandler.SchemeName, null);
}
else
{
    throw new InvalidOperationException("Auth:Authority must be configured outside Development.");
}

builder.Services.AddAuthorization(o =>
{
    o.AddPolicy("Dispatcher", p => p.RequireRole("dispatcher", "admin"));
    o.AddPolicy("Admin", p => p.RequireRole("admin"));
});

// ---- Abuse hardening (M4) ----
// Payload cap: nothing legitimate exceeds a WhatsApp media download (~16MB).
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 20 * 1024 * 1024);

// Per-IP fixed-window rate limit on everything public. Generous for the console's
// polling (a duty officer's session stays far under it); real per-reporter limits are
// Stage 0's job (D-008) — this layer only blunts raw endpoint hammering.
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.GlobalLimiter = System.Threading.RateLimiting.PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
        System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
            ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
            {
                PermitLimit = 600,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            }));
});

// ---- Triage services (M1) ----
builder.Services.Configure<TriageOptions>(builder.Configuration.GetSection("Triage"));
builder.Services.AddSingleton(sp =>
    sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<TriageOptions>>().Value);

builder.Services.Configure<First10.Application.Retention.RetentionOptions>(
    builder.Configuration.GetSection("Retention"));
builder.Services.AddSingleton(sp =>
    sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<First10.Application.Retention.RetentionOptions>>().Value);
if (!builder.Environment.IsEnvironment("Testing"))
{
    builder.Services.AddHostedService<First10.Api.Retention.RetentionBootstrapper>();
}

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

// D-009 blur gate: fully in-process face detection + irreversible blur, BEFORE persistence,
// BEFORE any external API. SecureMediaIngest is the only legal path to IMediaStore.SaveAsync
// (pinned by an architecture test).
builder.Services.Configure<BlurOptions>(builder.Configuration.GetSection("Blur"));
builder.Services.AddSingleton<IFaceBlurrer>(sp => new UltraFaceBlurrer(
    sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<BlurOptions>>().Value,
    sp.GetRequiredService<ILogger<UltraFaceBlurrer>>()));
// Videos: frames extracted in-scope (ffmpeg), each frame blurred, only a blurred
// contact sheet persisted — raw video never touches the store (D-019).
builder.Services.AddSingleton<IVideoFrameExtractor, FfmpegVideoFrameExtractor>();
builder.Services.AddScoped<SecureMediaIngest>();

// Short-lived signed media URLs (§7.1): the signature is the serve authorization.
// Outside Development the key MUST come from configuration (vault-backed in pilot) —
// refusing to boot beats silently signing with a known dev key.
var signingKey = builder.Configuration["Media:SigningKey"];
if (string.IsNullOrWhiteSpace(signingKey))
{
    if (!builder.Environment.IsDevelopment() && !builder.Environment.IsEnvironment("Testing"))
    {
        throw new InvalidOperationException("Media:SigningKey must be configured outside Development.");
    }
    signingKey = "first10-dev-only-signing-key";
}
builder.Services.AddSingleton(new MediaUrlSigner(
    System.Text.Encoding.UTF8.GetBytes(signingKey),
    TimeSpan.FromMinutes(builder.Configuration.GetValue("Media:SignedUrlLifetimeMinutes", 5))));

// AI services: LLM-backed behind IChatClient when a key is configured (D-003),
// heuristic/null fallbacks otherwise so the pipeline runs offline (dev/CI/tests).
var openAiKey = builder.Configuration["OpenAI:ApiKey"];
if (!string.IsNullOrWhiteSpace(openAiKey))
{
    var openAi = new OpenAIClient(openAiKey);
    var model = builder.Configuration["OpenAI:Model"] ?? "gpt-4o-mini";
    builder.Services.AddSingleton<IChatClient>(openAi.GetChatClient(model).AsIChatClient());
    // Every LLM service is wrapped in a heuristic-fallback decorator (D-008): an OpenAI
    // outage degrades triage quality, it never loses a report.
    builder.Services.AddSingleton<IIntentClassifier>(sp => new ResilientIntentClassifier(
        new ChatIntentClassifier(sp.GetRequiredService<IChatClient>()),
        new HeuristicIntentClassifier(),
        sp.GetRequiredService<ILogger<ResilientIntentClassifier>>()));
    builder.Services.AddSingleton<IIncidentExtractor>(sp => new ResilientIncidentExtractor(
        new ChatIncidentExtractor(sp.GetRequiredService<IChatClient>()),
        new HeuristicIncidentExtractor(),
        sp.GetRequiredService<ILogger<ResilientIncidentExtractor>>()));
    builder.Services.AddSingleton<ITimelineSummarizer>(sp => new ResilientTimelineSummarizer(
        new ChatTimelineSummarizer(sp.GetRequiredService<IChatClient>()),
        new HeuristicTimelineSummarizer(),
        sp.GetRequiredService<ILogger<ResilientTimelineSummarizer>>()));
    builder.Services.AddSingleton<ITranscriber>(sp => new ResilientTranscriber(
        new WhisperTranscriber(
            openAi.GetAudioClient(builder.Configuration["OpenAI:SttModel"] ?? "whisper-1")),
        sp.GetRequiredService<ILogger<ResilientTranscriber>>()));
}
else
{
    builder.Services.AddSingleton<IIntentClassifier, HeuristicIntentClassifier>();
    builder.Services.AddSingleton<IIncidentExtractor, HeuristicIncidentExtractor>();
    builder.Services.AddSingleton<ITimelineSummarizer, HeuristicTimelineSummarizer>();
    builder.Services.AddSingleton<ITranscriber, NullTranscriber>();
}

// ---- Telegram channel adapter ----
// Active when a bot token is configured (user-secrets in dev, vault in pilot).
// Long polling: no public URL or webhook needed — works from a laptop and the
// pilot VM alike. Without a token the channel simply doesn't exist.
var telegramToken = builder.Configuration["Telegram:BotToken"];
if (!string.IsNullOrWhiteSpace(telegramToken))
{
    // Bot API URLs embed the token — HttpClient's info-level request logging would
    // write the secret into every log line. Warnings and errors only.
    builder.Logging.AddFilter("System.Net.Http.HttpClient.telegram", LogLevel.Warning);
    builder.Services.AddHttpClient(); // shared factory for the bot client
    builder.Services.AddSingleton(sp => new First10.Infrastructure.Telegram.TelegramBotApi(
        sp.GetRequiredService<IHttpClientFactory>().CreateClient("telegram"), telegramToken));
    builder.Services.AddSingleton<First10.Domain.Abstractions.IOutboundChannelSender,
        First10.Infrastructure.Telegram.TelegramOutboundSender>();
    if (!builder.Environment.IsEnvironment("Testing"))
    {
        builder.Services.AddHostedService<First10.Api.Telegram.TelegramPollingService>();
    }
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

    // Seed placeholder micro-instruction templates (UNAPPROVED — the clinical advisor's
    // signed-off set replaces these; unapproved templates only send when the dev-only
    // Triage:AllowUnapprovedTemplates flag is set).
    await TemplateSeeder.SeedPlaceholdersAsync(db);
}

app.UseCors(SpaCors);
app.UseRateLimiter();

// Webhook signature gate (R11): every POST to /api/webhooks must carry a valid
// X-Hub-Signature-256; with no Meta:AppSecret configured the path is dead entirely —
// the WhatsApp adapter (M5) cannot be exposed unsigned by accident.
var metaAppSecret = app.Configuration["Meta:AppSecret"];
app.UseMiddleware<First10.Api.Webhooks.MetaWebhookSignatureMiddleware>(
    new First10.Api.Webhooks.MetaWebhookOptions(
        string.IsNullOrWhiteSpace(metaAppSecret)
            ? null
            : new First10.Api.Webhooks.MetaWebhookSignatureValidator(
                System.Text.Encoding.UTF8.GetBytes(metaAppSecret))));

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHub<First10.Api.Hubs.ConsoleHub>("/hubs/console").RequireAuthorization("Dispatcher");

app.Run();

// Exposed for WebApplicationFactory-based tests.
public partial class Program;
