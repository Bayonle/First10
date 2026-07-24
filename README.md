# First10

AI-powered bystander crash reporting for the Lagos–Ibadan Expressway, piloted with FRSC Ogun Command. A bystander sends a photo + voice note on WhatsApp; First10 triages it, extracts a structured incident ticket, replies with clinically pre-approved safety instructions, and puts the incident in front of an FRSC dispatcher in seconds.

**Project docs:** [`first10-project-paper.md`](first10-project-paper.md) (scope/governance) · [`docs/`](docs/) (decisions, milestones, status) · [`CLAUDE.md`](CLAUDE.md) (session conventions)

## Run locally

Prerequisites: .NET 10 SDK, Node 22+, Docker (for the Postgres container Aspire manages),
and **ffmpeg** on PATH (`brew install ffmpeg` / `apt install ffmpeg`) — video evidence is
converted to blurred contact sheets at ingest (D-019); without ffmpeg, videos are refused.

```sh
dotnet run --project src/First10.AppHost
```

That boots everything: Postgres (container), the API, and the Vite dev server — plus the Aspire dashboard (URL printed at startup) with logs and traces for all three.

Then open:

- **http://localhost:5173/local-chat** — dev cockpit: send messages as a fake bystander
- **http://localhost:5173/console** — dispatcher console: watch tickets appear live

Send a message in one tab, see the incident in the other. That loop is the whole point.

## Enabling the AI services (OpenAI)

The pipeline runs fully offline by default (heuristic classifier/extractor, no STT).
To activate the LLM-backed intent classifier, multimodal extractor, and Whisper STT,
provide the key via **user-secrets** — never in `appsettings*.json` (those are committed):

```sh
cd src/First10.Api
dotnet user-secrets set "OpenAI:ApiKey" "sk-…"
```

Model choices live in config (`OpenAI:Model`, default `gpt-4o-mini`; `OpenAI:SttModel`,
default `whisper-1`). DI switches implementations automatically when the key is present —
no code change. Set a hard monthly spend cap on the OpenAI account before use
(project paper §3.3 control 3).

All LLM calls sit behind heuristic-fallback decorators: if OpenAI is down, rate-limited,
or returns garbage, triage degrades to keyword heuristics and no report is lost (D-008).

## Enabling the Telegram channel (real phone testing, no Meta approval needed)

1. In Telegram, talk to **@BotFather** → `/newbot` → pick a name and username. It
   replies with a bot token (`123456:ABC-…`).
2. ```sh
   cd src/First10.Api
   dotnet user-secrets set "Telegram:BotToken" "123456:ABC-…"
   ```
3. Restart the stack. The log line `Telegram adapter active (long polling)` confirms it.
4. Open your bot in Telegram and report a crash — text, photo, voice note, video,
   location pin all work. Replies (challenges, micro-instructions, loop closures)
   arrive in the same chat; the incident appears in the console like any other.

Long polling means no public URL, webhook, or certificate — it works from a laptop.
Media goes through the same D-009 gate as every channel: photos blurred, videos
become blurred contact sheets, before anything is persisted. Without a token the
channel simply doesn't exist.

## Security & privacy configuration (M4)

Everything privacy-critical is structural — these keys just parameterize it:

| Key | Dev default | Production |
|---|---|---|
| `Media:SigningKey` | built-in dev key | **required** — API refuses to boot without it |
| `Media:SignedUrlLifetimeMinutes` | 5 | 5 |
| `Auth:Authority` / `Auth:Audience` | absent → DevAuth (auto-login as `dev-console`) | **required** — OIDC bearer (Entra ID etc.) |
| `Retention:MediaRetentionDays` | 30 (provisional — lawyer confirms) | lawyer's number |
| `Meta:AppSecret` | absent → `/api/webhooks` is dead | required for the WhatsApp adapter |
| `Blur:ModelPath` | RFB-640 beside binaries | same (RFB-320 available for low-latency) |

Inbound images are face-blurred **in memory before persistence** (UltraFace ONNX,
in-process — nothing unblurred ever touches disk, the console, or an external API).
Media is served only via 5-minute HMAC-signed URLs; every ticket view and URL issuance
is audit-logged; a retention sweep deletes media past the window with audit rows.
The console shows a hard red banner if any message ever dead-letters.

## Tests

```sh
dotnet test
```

## Reset local data

Aspire's orchestrator (`dcp`) survives a naive `pkill First10` and resurrects containers —
stop everything before removing volumes or the wipe silently doesn't stick:

```sh
pkill -f First10; pkill -f dcpctrl; pkill -f "aspire"; sleep 2
docker ps --format '{{.Names}}' | grep -E '^(pg-|minio-)' | xargs -r docker rm -f
docker volume rm first10-pg-data first10-minio-data
```

Next AppHost run starts with an empty database (migrations re-apply) and empty MinIO.

## Layout

| Path | What |
|---|---|
| `src/First10.Domain` | Entities, channel envelope, enums — no dependencies |
| `src/First10.Infrastructure` | EF Core DbContext, migrations |
| `src/First10.Application` | Wolverine handlers (the pipeline) |
| `src/First10.Api` | Controllers, SignalR hub, Wolverine/outbox wiring |
| `src/First10.AppHost` | Aspire local orchestration |
| `web/` | React SPA — dispatcher console + dev cockpit |
