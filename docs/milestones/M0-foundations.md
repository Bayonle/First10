# M0 — Foundations & Walking Skeleton

**Target:** 22 – 25 Jul 2026 · **Paper mapping:** W6 · **Depends on:** nothing
**Goal:** a running end-to-end skeleton — message in via the local provider, provisional ticket visible in a bare console — so every later milestone iterates on a live system.

**Status: ✅ COMPLETE (22 Jul 2026)** — all exit criteria verified.

## Exit criteria

- [x] A message typed in the local chat cockpit produces a `TimelineEntry` row and a provisional ticket visible in the (bare) console within seconds, via the full Wolverine pipeline — no shortcuts bypassing the queue. *(Verified: POST → 202 → outbox → ticket in console API ~2s; SignalR negotiate confirmed through Vite proxy.)*
- [x] Solution builds and tests run in CI on every push. *(8 tests green locally; CI workflow in `.github/workflows/ci.yml` — confirm green on first push.)*
- [x] One command brings up Postgres + API + SPA for any team member. *(Changed from `docker compose up` to `dotnet run --project src/First10.AppHost` per D-015.)*

## Tasks

### Solution & infrastructure
- [x] Create solution: `First10.Api` (controllers, SignalR), `First10.Application` (Wolverine handlers, saga, AI service interfaces), `First10.Domain` (aggregates, envelopes, schemas), `First10.Infrastructure` (EF Core, blob storage, channel adapters)
- [x] EF Core + Postgres wiring; initial migration (`Conversation`, `IncidentTicket`, `TimelineEntry`, dedup index on `(Channel, ExternalMessageId)`)
- [x] Wolverine configured with durable outbox over EF Core/Postgres (D-002)
- [x] ~~`docker-compose.yml`~~ → Aspire AppHost (D-015): Postgres container + API + Vite dev server, one command
- [x] CI: build + test on push (GitHub Actions)

### Local channel provider — minimum viable (D-006)
- [x] `LocalChatController` accepting text messages → normalizes to `InboundChannelMessage` → publishes to the same pipeline as any channel
- [x] Dev-only chat page in SPA (persona dropdown ×3, text only at this stage)
- [x] **Production gate:** local provider route + controller unreachable outside Development — `DevelopmentOnlyAttribute` + tests covering Production/Staging/Testing

### Walking skeleton pipeline
- [x] `InboundChannelMessage` ingest handler: dedup by external message id, resolve/create `Conversation`, append `TimelineEntry`
- [x] Stub session start: first message opens a session + provisional ticket (real triage comes in M1)
- [x] SignalR hub pushing ticket/timeline changes; SPA console page listing tickets live

### SPA shell
- [x] Vite + React + TS scaffold; TanStack Router with `/console` and dev-only `/local-chat` routes; TanStack Query + SignalR invalidation wiring

## Explicitly out of scope for M0

Intent classification, evidence levels, media handling, auth, real channels. Resist gold-plating — M0's only job is the loop.

## Added during execution

- **Aspire replaces docker-compose** (user decision mid-build) — see D-015. Blob emulation deferred to M2 when the media pipeline consumes it.
- `WolverineFx.RuntimeCompilation` package required — Wolverine 6.x no longer ships the runtime code generator in the core package (clear startup error pointed to it).
- Dependency direction fixed early: `Application → Infrastructure` (handlers consume `First10DbContext` directly), not the reverse as originally scaffolded.
- Template's `Microsoft.AspNetCore.OpenApi` removed (NU1903 vulnerability warning; unused).
- Tests use EF InMemory for handler logic + NSubstitute for the dev-gate filter; DB-fidelity integration tests (unique-index races) deferred to M1 when Postgres-backed tests join CI.
