# First10 — Project Status

> Update at the end of every working session. This file is the handoff between sessions — assume the reader (human or AI) has zero short-term memory and 2 minutes.

**Last updated:** 2026-07-22
**Current milestone:** [M1 — Intake & Triage](milestones/M1-intake-triage.md) ✅ **COMPLETE** → next is [M2 — Session & Extraction](milestones/M2-session-extraction.md)
**Overall:** the triage funnel is live end-to-end. A pidgin text report gets classified, opens a Challenge-disposition ticket, and the reporter receives an elicitation reply; photos fast-track evidence-first; floods trip the console banner and cap dispositions; greetings get canned replies with no ticket. Media flows through MinIO. 42 tests green.

## Next task

Start M2: the `ReportingSession` saga (states, promotion rule, pin-reminder/challenge-expiry timeouts via `TimeProvider`), then dedup/merge (200m/5min → shared incidents, which unlocks `AUTO_VERIFY`), then AI extraction + STT + micro-instruction template store.

## In flight

_(clean break — M1 closed, M2 not started)_

## Blockers

- **OpenAI API key still needed** — now blocks *two* things: activating `ChatIntentClassifier` (built, tested only via heuristic twin) and M2's extraction + STT (Whisper). Decide account ownership + spending cap (§3.3 control 3). Set `OpenAI:ApiKey` via user-secrets; the DI switch is automatic.

## External dependency status

| Dependency | Blocks | Owner | Status |
|---|---|---|---|
| Meta Business verification + WhatsApp Cloud API application | M5 | Solution Lead | ☐ **NOT STARTED — apply now, 5–10 business days** |
| OpenAI API key + spend cap | Stage 1 LLM, M2 extraction/STT | Team Leader | ☐ not started |
| Clinical template library v1 | M2 content (placeholders OK) | R&A Lead + clinical advisor | ☐ not started |
| Labelled test set (~100 photos + voice notes) + viral-image corpus seed | M5 §7.4 protocols, pHash corpus | R&A Lead (via FRSC commander) | ☐ not started |
| NDPA legal review | G4 / soft launch | R&A Lead | ☐ not started |
| Corridor waypoint verification with FRSC | Geofence accuracy (soft launch) | Solution Lead | ☐ not started |

## Milestone scoreboard

| Milestone | Window | Status |
|---|---|---|
| M0 Foundations | 22–25 Jul | ✅ complete 22 Jul |
| M1 Intake & Triage | 26–29 Jul | ✅ complete 22 Jul (a week early) |
| M2 Session & Extraction | 30 Jul–3 Aug | ☐ next — can start early |
| M3 Dispatch Console | 2–7 Aug | ☐ (badges/flood banner/media rendering already landed via M1) |
| M4 Privacy & Hardening | 6–9 Aug | ☐ |
| M5 Pilot Readiness (G3+G4) | 8–11 Aug | ☐ |

## Session log

_(newest first — one line per session: date, what moved)_

- **2026-07-22 (3)** — M1 built and verified: triage funnel (Stage 0 rate-limit/reputation/pHash/geofence/flood → Stage 1 heuristic + IChatClient classifiers → Stage 2 DispositionEngine with D-008 ceilings), challenge elicitation through the outbox, MinIO object storage via Aspire (D-016), cockpit upgrades (personas, image/stock/voice/pins, scenario runner), console badges + flood banner. 42/42 tests. E2E verified: pidgin challenge flow, canned replies, photo fast-track through MinIO, spam-flood → banner + caps.
- **2026-07-22 (2)** — M0 built and verified end-to-end: .NET 10 solution, Wolverine outbox on Postgres, local channel provider + dev gate, React SPA with TanStack + SignalR, Aspire AppHost (D-015), CI. 8/8 tests.
- **2026-07-22 (1)** — Architecture + stack decided; decision log D-001…D-014, milestone docs M0–M5, CLAUDE.md created. No code yet.
