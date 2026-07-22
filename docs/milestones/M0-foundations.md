# M0 — Foundations & Walking Skeleton

**Target:** 22 – 25 Jul 2026 · **Paper mapping:** W6 · **Depends on:** nothing
**Goal:** a running end-to-end skeleton — message in via the local provider, provisional ticket visible in a bare console — so every later milestone iterates on a live system.

## Exit criteria

- [ ] A message typed in the local chat cockpit produces a `TimelineEntry` row and a provisional ticket visible in the (bare) console within seconds, via the full Wolverine pipeline — no shortcuts bypassing the queue.
- [ ] Solution builds and tests run in CI on every push.
- [ ] `docker compose up` brings up Postgres + API + SPA for any team member.

## Tasks

### Solution & infrastructure
- [ ] Create solution: `First10.Api` (controllers, SignalR), `First10.Application` (Wolverine handlers, saga, AI service interfaces), `First10.Domain` (aggregates, envelopes, schemas), `First10.Infrastructure` (EF Core, blob storage, channel adapters)
- [ ] EF Core + Postgres wiring; initial migration (`Conversation`, `IncidentTicket`, `TimelineEntry`, dedup index on `(Channel, ExternalMessageId)`)
- [ ] Wolverine configured with durable outbox over EF Core/Postgres (D-002)
- [ ] `docker-compose.yml` (Postgres, API, SPA dev server); local blob emulation (Azurite or MinIO)
- [ ] CI: build + test on push (GitHub Actions per paper §8.2 tooling)

### Local channel provider — minimum viable (D-006)
- [ ] `LocalChatController` accepting text messages → normalizes to `InboundChannelMessage` → publishes to the same pipeline as any channel
- [ ] Dev-only chat page in SPA (single persona, text only at this stage)
- [ ] **Production gate:** local provider route + controller unreachable outside Development environment — verify with a test

### Walking skeleton pipeline
- [ ] `RawInboundReceived` ingest handler: dedup by external message id, resolve/create `Conversation`, append `TimelineEntry`
- [ ] Stub session start: any first message opens a session + provisional ticket (real triage comes in M1)
- [ ] SignalR hub pushing ticket/timeline changes; SPA console page listing tickets live (unstyled is fine)

### SPA shell
- [ ] Vite + React + TS scaffold; TanStack Router with `/console` and dev-only `/local-chat` routes; TanStack Query + SignalR invalidation wiring

## Explicitly out of scope for M0

Intent classification, evidence levels, media handling, auth, real channels. Resist gold-plating — M0's only job is the loop.

## Added during execution

_(append discovered work here)_
