# First10 — Project Status

> Update at the end of every working session. This file is the handoff between sessions — assume the reader (human or AI) has zero short-term memory and 2 minutes.

**Last updated:** 2026-07-22
**Current milestone:** [M2 — Session & Extraction](milestones/M2-session-extraction.md) **~85% built, core verified live** → remainder + [M3 console](milestones/M3-dispatch-console.md) next
**Overall:** the full reporter experience runs end-to-end: saga-driven durable timers (unprompted 30s pin reminder verified live), 200m/5min corroboration merging two reporters into one AUTO_VERIFY/Promoted incident, async extraction (severity/template selection) with micro-instructions delivered seconds after the first message, STT + extraction + classification all behind swap-ready interfaces. 76 tests green.

## Next task

Finish M2 remainder (timeline summarizer; template audio; chat-impl activation when the key lands) **or** start M3 dispatcher actions (dispatched/arrived/transported + outcome marking) — M3 recommended: it unblocks the loop-closure story and the late-reporter path.

## In flight

- M2 remainder items listed in the milestone doc (~15%: summarizer, audio recordings, chat-impl accuracy pass).

## Blockers

- **OpenAI API key still needed** — blocks activation of `ChatIntentClassifier`, `ChatIncidentExtractor`, and `WhisperTranscriber` (all built and wired; DI switches automatically on `OpenAI:ApiKey`). Decide account ownership + spending cap (§3.3 control 3).

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

- **2026-07-22 (12)** — Wild-human-behavior sweep (10 scenarios imagining real corridor chaos): panic fragments across 5 rapid texts assembled into one coherent ticket; ALL-CAPS misspellings triaged fine; lone "help" → guided canned reply; two-incidents-in-one-message summarized honestly; exclamation-only reports open low-info tickets; "hm" voice note → Review (ear decides); gratitude gets ask-restatement. Found+fixed 4: (1) pin corrections were silently discarded — an Abuja fat-finger stuck forever; later pins now replace location + re-evaluate the corridor flag both directions; (2) photo-mismatch false-flagged every text-only ticket (model returns false on no-photo input) — now guarded on a real image; (3) retractions were diluted into incident facts ("no injuries reported") — summaries now lead "REPORTER RETRACTED:"; (4) Igbo/mixed exclamations got Yoruba replies — unclear language now falls back to pidgin, the corridor lingua franca. All four verified live post-fix. 81 tests. Follow-up: screenshot-as-evidence vision case needs a proper run with real crash photos (M5 labelled set).
- **2026-07-22 (11)** — First live-LLM run (key active): 12 real-life cases through chat classifier + multimodal extractor + Whisper. Wins: keyword-free pidgin/Yoruba triage, "how do I report accident?" correctly no-ticket, fender-bender → severity Low, casualty extraction from text AND audio, fire/okada template selection, spam starved. Found+fixed: gibberish voice silently dropped (voice now ALWAYS triages — dispatcher's ear decides), stale saga timers dead-lettering (NotFound no-ops), missing transient-error retry policy, unused photo_matches_narrative (now flags+caps), gazetteer added to extractor prompt ("Carra"→Kara; motor=car in pidgin). 78 tests, 0 dead letters.
- **2026-07-22 (10)** — M2 core built and verified live: `ReportingSessionSaga` with durable Wolverine timers (pin reminder fired unprompted 30s after a silent text-only report — the R5c flow, proactive at last), 200m/5min corroboration (reporter B's pin merged their ticket into A's → AUTO_VERIFY + Promoted + 2-reporter relay timeline with per-reporter identity; independence enforced via timeline history so reporters can't corroborate themselves), async extraction cascade (heuristic now, `ChatIncidentExtractor` ready; fire report → severity High → pidgin `rta_fire` micro-instruction seconds after first message), STT interface (Whisper ready, transcript-beside-audio in console), clinically-gated template store (unapproved templates only send under the dev flag — G3 structurally enforced). Migration #5; 76 tests.
- **2026-07-22 (9)** — Live proof session for the anti-spam claims: (1) pHash — WhatsApp-style forward (½ res, q50, 12× smaller file) flagged `reused-image`, different scene passes clean; (2) reputation — blocked reporter leaves zero footprint, low-trust photo capped at Review; (3) rate limit — 31 opens from one number → 30 tickets + silent 31st drop while another number works normally. Proving pHash exposed a real bug: low-texture images (gradients, uniform, dark/blurry) produce degenerate dHashes that collide — a night-time crash photo could have been false-flagged as reused. Fixed: degenerate hashes (≤4 or ≥60 bits) are excluded from reuse detection and the corpus; unit tests rebuilt on textured block-noise scenes. 65 tests.
- **2026-07-22 (8)** — Agent-driven edge-case sweep (agent-browser + API probes, 12 scenarios): verified clean — emoji/pidgin triage, double-submit dedup, question→incident sequencing, ghost-media grace, cross-persona pHash reuse detection, flood banner + badges visually. Found & fixed 3 bugs: pin-first reports were asked for the pin again (now get "location received, send photo"); >8192-char texts dead-lettered = silently lost reports (now truncated defensively); undefined message kinds accepted (now 400). README gains the full Aspire reset procedure (dcp survives naive pkill — the earlier volume wipe hadn't stuck). 60 tests.
- **2026-07-22 (7)** — Pending-ask reminders ("never silent, never nagging"): messages that earn no other reply (mid-flow questions, duplicate photos) now restate whatever the session awaits — pin / photo / full ask — or report status when complete (`StatusUnderReview`, review-only wording per R1e). Throttled: 30s for texts, 120s for media, tracked per ticket. 57 tests; screenshot flow replayed green.
- **2026-07-22 (6)** — Session max-age cap (default 60 min) added after live testing hit the treadmill case: frequent messages kept resetting the inactivity clock, so ancient tickets swallowed everything silently ("no response on any persona"). Boundary now fires on inactivity OR age. 53 tests. Verified live: persona with a 90-min-old relic ticket gets a fresh incident + challenge reply again.
- **2026-07-22 (5)** — Session boundary pulled forward from M2 after console testing showed one conversation = one eternal ticket (15-min-later photo enriched the morning's incident). Lazy inactivity check at ingest (default 15 min): unanswered challenges → `ExpiredUnverified` (visible; human makes the kill call), evidenced tickets stay pending, next message opens a fresh incident. 51 tests.
- **2026-07-22 (4)** — Reporter feedback loop fixed after cockpit testing surfaced silence-after-pin and canned-reply spam: per-contribution acks (PinReceivedAck / LocationPinRequest / ReportAck, EN+pidgin+yoruba), canned-reply throttle, `LocationResolvedAt` as first-class ticket state + console badge. 47 tests. Full text→pin→photo loop verified live; pHash incidentally caught a reused test image and capped it (working as designed).
- **2026-07-22 (3)** — M1 built and verified: triage funnel (Stage 0 rate-limit/reputation/pHash/geofence/flood → Stage 1 heuristic + IChatClient classifiers → Stage 2 DispositionEngine with D-008 ceilings), challenge elicitation through the outbox, MinIO object storage via Aspire (D-016), cockpit upgrades (personas, image/stock/voice/pins, scenario runner), console badges + flood banner. 42/42 tests. E2E verified: pidgin challenge flow, canned replies, photo fast-track through MinIO, spam-flood → banner + caps.
- **2026-07-22 (2)** — M0 built and verified end-to-end: .NET 10 solution, Wolverine outbox on Postgres, local channel provider + dev gate, React SPA with TanStack + SignalR, Aspire AppHost (D-015), CI. 8/8 tests.
- **2026-07-22 (1)** — Architecture + stack decided; decision log D-001…D-014, milestone docs M0–M5, CLAUDE.md created. No code yet.
