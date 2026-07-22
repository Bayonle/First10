# M1 — Intake, Triage Funnel & Anti-Spam

**Target:** 26 – 29 Jul 2026 · **Paper mapping:** W6/W7 · **Depends on:** M0
**Goal:** the full triage funnel (D-008) live behind the local provider — any message classifies, dispositions, and routes correctly, including hostile input.

## Exit criteria

- [ ] Every disposition path (`AUTO_VERIFY`, `FAST_TRACK`, `REVIEW`, `CHALLENGE`, `DROP`) reachable and demonstrated via local-provider scenarios.
- [ ] Text-only report → challenge sent, provisional ticket visible in review queue flagged "awaiting evidence" — never silently dropped.
- [ ] Duplicate/re-sent image detected by pHash; rate-limited number throttled; spam flood caps dispositions at REVIEW.
- [ ] Stage 1 intent call correct on the seeded Pidgin/Yoruba example set (≥ 90% on the seed set).

## Tasks

### Stage 0 — deterministic gates
- [ ] Per-number rate limiting (config: N new-incident attempts / window) with reputation-aware tiers
- [ ] Blocklist + reporter reputation store (trained volunteers seeded high-trust; dispatcher-confirmed-false sticky low)
- [ ] Conversation-state routing: active session → route as session message, skip intent
- [ ] Perceptual hash (pHash) on inbound images; corpus table + seeded known-viral crash images; near-match threshold tuning
- [ ] Corridor geofence check (polygon buffer for Berger–Mowe) — flags, never drops
- [ ] Flood detector: N distinct-number incidents / M minutes above baseline → console banner + disposition cap (R11)

### Stage 1 — intent classification (D-003, D-008)
- [ ] `IIntentClassifier` behind `IChatClient`; structured output `IntentResult` (intent, language, urgency, confidence)
- [ ] Prompt with bias-toward-incident rule + 5–10 few-shot examples per language (English, Pidgin, Yoruba)
- [ ] Prompt/schema versioning (log which prompt version classified each message — needed for the weekly accuracy review)

### Stage 2 — disposition engine
- [ ] Evidence-level model (photo+voice / photo / voice / text) with disposition ceilings per D-008
- [ ] Disposition persisted on ticket + timeline; every transition audit-logged
- [ ] CHALLENGE flow: elicitation reply command published (send path is channel-agnostic; local provider renders it)

### Local cockpit upgrades (D-006)
- [ ] Multiple personas (switch sender identity)
- [ ] Image upload + stock crash-photo shortcuts
- [ ] Browser `MediaRecorder` voice notes (feeds real audio; STT lands in M2 — store + timeline now)
- [ ] Map-click location pin emitting `LocationPin` envelope
- [ ] Scenario runner v1: JSON-scripted envelope sequences with timing (`text-only-then-timeout`, `spam-flood`, `duplicate-image`)

## Metrics wired in this milestone

Disposition counts, stage latencies, elicitation-conversion rate — one `dispositions` table queryable for the panel deck (paper objective 3: FP < 5%, legit latency ≤ 30s).

## Added during execution

_(append discovered work here)_
