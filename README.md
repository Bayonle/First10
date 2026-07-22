# First10

AI-powered bystander crash reporting for the Lagos–Ibadan Expressway, piloted with FRSC Ogun Command. A bystander sends a photo + voice note on WhatsApp; First10 triages it, extracts a structured incident ticket, replies with clinically pre-approved safety instructions, and puts the incident in front of an FRSC dispatcher in seconds.

**Project docs:** [`first10-project-paper.md`](first10-project-paper.md) (scope/governance) · [`docs/`](docs/) (decisions, milestones, status) · [`CLAUDE.md`](CLAUDE.md) (session conventions)

## Run locally

Prerequisites: .NET 10 SDK, Node 22+, Docker (for the Postgres container Aspire manages).

```sh
dotnet run --project src/First10.AppHost
```

That boots everything: Postgres (container), the API, and the Vite dev server — plus the Aspire dashboard (URL printed at startup) with logs and traces for all three.

Then open:

- **http://localhost:5173/local-chat** — dev cockpit: send messages as a fake bystander
- **http://localhost:5173/console** — dispatcher console: watch tickets appear live

Send a message in one tab, see the incident in the other. That loop is the whole point.

## Tests

```sh
dotnet test
```

## Layout

| Path | What |
|---|---|
| `src/First10.Domain` | Entities, channel envelope, enums — no dependencies |
| `src/First10.Infrastructure` | EF Core DbContext, migrations |
| `src/First10.Application` | Wolverine handlers (the pipeline) |
| `src/First10.Api` | Controllers, SignalR hub, Wolverine/outbox wiring |
| `src/First10.AppHost` | Aspire local orchestration |
| `web/` | React SPA — dispatcher console + dev cockpit |
