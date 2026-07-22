# M1 — Intake, Triage Funnel & Anti-Spam

**Target:** 26 – 29 Jul 2026 · **Paper mapping:** W6/W7 · **Depends on:** M0
**Goal:** the full triage funnel (D-008) live behind the local provider — any message classifies, dispositions, and routes correctly, including hostile input.

**Status: ✅ COMPLETE (22 Jul 2026)** — all exit criteria verified (one scoped note below). Finished a week ahead of window.

## Exit criteria

- [x] Every disposition path reachable and demonstrated via local-provider scenarios. *(Verified live: Challenge, FastTrack, Review-via-flood-cap, Drop (tests), None/canned-reply. `AUTO_VERIFY` is by design unreachable until M2 — it requires multi-reporter corroboration (D-008).)*
- [x] Text-only report → challenge sent, provisional ticket visible flagged as challenge — never silently dropped. *(E2E: pidgin report → Challenge disposition + pidgin elicitation reply in conversation.)*
- [x] Duplicate/re-sent image detected by pHash; rate-limited number throttled; spam flood caps dispositions at REVIEW. *(pHash: unit tests, re-encode+resize ≤10 hamming; rate limit + flood: tests + live spam-flood scenario → banner active, tickets flagged `flood-active` and capped.)*
- [x] Stage 1 intent correct on the seeded Pidgin/Yoruba example set. *(Heuristic classifier: 42-test suite green incl. diacritic-stripped Yoruba. LLM classifier untested until the OpenAI key is provisioned — see Blockers in STATUS.)*

## Tasks

### Stage 0 — deterministic gates
- [x] Per-number rate limiting (config `Triage:MaxNewIncidentsPerWindow`) — *uniform, not yet reputation-tiered; tiering deferred until real-world data suggests it's needed*
- [x] Blocklist + reporter reputation store (`reporter_reputations`; blocked reporters dropped silently; volunteer seeding is a pilot-onboarding data task, M5)
- [x] Conversation-state routing: active session → enrich open ticket, skip intent
- [x] Perceptual hash (dHash, ImageSharp) + `media_assets` corpus table; threshold configurable — *seeding known-viral crash images is a data task alongside the labelled test set (M5)*
- [x] Corridor geofence (Berger–Mowe polyline, 2km buffer) — flags `outside-corridor`, never drops. *Waypoints approximate — verify with FRSC before soft launch (TODO in TriageOptions)*
- [x] Flood detector: window count → `GET /api/system/flood`, console banner, disposition cap (R11)

### Stage 1 — intent classification (D-003, D-008)
- [x] `IIntentClassifier` contract; `ChatIntentClassifier` behind `IChatClient` with structured output, bias-toward-incident rule + per-language few-shots — *activates automatically when `OpenAI:ApiKey` is configured*
- [x] `HeuristicIntentClassifier` fallback (keyword + diacritic normalization) so the funnel runs offline/CI
- [x] Classifier/prompt versioning: `ClassifierVersion` recorded per ticket (`heuristic-v1` / `chat-v1` / `evidence-first`)

### Stage 2 — disposition engine
- [x] `DispositionEngine` pure function: evidence ceilings, trust/flood/reuse caps, flag accumulation (12 unit tests on the D-008 table)
- [x] Disposition + evidence + flags + language persisted on ticket; transitions written as System timeline entries (visible, italicized in console per D-013)
- [x] CHALLENGE flow: `SendOutboundMessage` cascaded through outbox → Local sender renders reply in cockpit (EN/pidgin/Yoruba texts)

### Local cockpit upgrades (D-006)
- [x] Add-persona button (unlimited fake reporters)
- [x] Image upload + 3 stock-crash-photo generators (canvas → JPEG — re-sending the same stock exercises pHash)
- [x] `MediaRecorder` voice notes → uploaded → through the real media + timeline path (STT lands M2)
- [x] Location pins via corridor presets (Berger/Kara/Ibafo/Mowe + off-corridor Abuja) — *map-click picker deferred; presets cover every geofence case*
- [x] Scenario runner v1: `text-only-challenge`, `two-reporters`, `spam-flood`, `outside-corridor`, `non-incidents` — Wolverine-scheduled delivery (durable, ordered). *Code-defined catalog rather than JSON files; revisit if non-devs need to author scenarios*

### Metrics wired in this milestone
- [x] Disposition/evidence/flags/language queryable per ticket; challenge-conversion measurable via `ChallengeSentAt` + subsequent evidence level. *Dedicated `dispositions` history table deferred to M3 when outcome marking lands — that's when FP-rate math becomes real.*

## Added during execution

- **MinIO via Aspire replaces the filesystem-only plan** (user decision mid-build) — see D-016. S3-compatible `IMediaStore`; filesystem store kept as fallback.
- **ImageSharp pinned to 3.1.x** — v4.0 requires a commercial license key at build time (license note in D-016).
- Yoruba keyword bug caught by tests: "ìjàǹbá" normalizes to "ijan**b**a" not "ijam**b**a" — a good reminder that the labelled corridor test set (M5) matters more than dictionary intuition.
- Voice notes currently triage as `NewIncident`/low-confidence with `voice-untranscribed` marker — correct bias until STT lands in M2.
- Console rendering: images inline, audio playable, System notes italicized — early M3 groundwork that fell out of verifying M1.
