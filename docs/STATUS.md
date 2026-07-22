# First10 — Project Status

> Update at the end of every working session. This file is the handoff between sessions — assume the reader (human or AI) has zero short-term memory and 2 minutes.

**Last updated:** 2026-07-22
**Current milestone:** [M0 — Foundations](milestones/M0-foundations.md) ✅ **COMPLETE** → next is [M1 — Intake & Triage](milestones/M1-intake-triage.md)
**Overall:** walking skeleton is live. `dotnet run --project src/First10.AppHost` boots Postgres + API + SPA; a message sent in `/local-chat` flows through the Wolverine outbox pipeline and appears as a provisional ticket in `/console` within ~2s. 8 tests green.

## Next task

Start M1: Stage 0 deterministic gates (rate limiting, conversation-state routing) — the first pieces of the triage funnel. Then the Stage 1 intent classifier behind `IChatClient` (needs an OpenAI API key in user-secrets — not yet provisioned).

## In flight

_(clean break — M0 closed, M1 not started)_

## Blockers

- **OpenAI API key needed before M1's Stage 1 intent work** (D-003). Decide account ownership + set spending cap (paper §3.3 control 3).

## External dependency status

| Dependency | Blocks | Owner | Status |
|---|---|---|---|
| Meta Business verification + WhatsApp Cloud API application | M5 | Solution Lead | ☐ **NOT STARTED — apply now, 5–10 business days** |
| Clinical template library v1 | M2 content (placeholders OK) | R&A Lead + clinical advisor | ☐ not started |
| Labelled test set (~100 photos + voice notes) | M5 §7.4 protocols | R&A Lead (via FRSC commander) | ☐ not started |
| NDPA legal review | G4 / soft launch | R&A Lead | ☐ not started |

## Milestone scoreboard

| Milestone | Window | Status |
|---|---|---|
| M0 Foundations | 22–25 Jul | ✅ complete 22 Jul (3 days early) |
| M1 Intake & Triage | 26–29 Jul | ☐ next — can start early |
| M2 Session & Extraction | 30 Jul–3 Aug | ☐ |
| M3 Dispatch Console | 2–7 Aug | ☐ |
| M4 Privacy & Hardening | 6–9 Aug | ☐ |
| M5 Pilot Readiness (G3+G4) | 8–11 Aug | ☐ |

## Session log

_(newest first — one line per session: date, what moved)_

- **2026-07-22 (2)** — M0 built and verified end-to-end: .NET 10 solution (Domain/Infrastructure/Application/Api + tests), Wolverine 6.21 durable outbox on Postgres, EF migration, local channel provider + dev gate (tested), React SPA (console + local-chat) with TanStack Router/Query + SignalR invalidation, **Aspire AppHost** for one-command local dev (D-015, replaced compose plan mid-build), CI workflow. 8/8 tests pass. Verified live: message → 202 → outbox → ticket in console, dedup drops redeliveries, zero dead letters.
- **2026-07-22 (1)** — Architecture + stack decided; decision log (D-001…D-014), milestone docs M0–M5, CLAUDE.md, and this file created. No code yet.
