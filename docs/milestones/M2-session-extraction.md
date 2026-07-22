# M2 — Reporting Session, Extraction & Micro-Instructions

**Target:** 30 Jul – 3 Aug 2026 · **Paper mapping:** W7 · **Depends on:** M1 · **External:** clinical template library v1 (placeholder templates acceptable until sign-off)
**Goal:** the full reporter experience — session saga with timeouts, AI extraction enriching the provisional ticket, micro-instructions and pin requests going out, dedup merging multi-reporter incidents.

## Exit criteria

- [ ] Two-personas-same-crash scenario: second report auto-verifies the incident, both sessions merge into one relay timeline, each reporter receives their own instructions.
- [ ] Text-only → challenge → photo+pin arrives → ticket enriches → PROMOTED, watched live in the console.
- [ ] Challenge unanswered → 30s reminder → expiry → ticket remains visible as unverified-expired (never auto-deleted).
- [ ] All saga timeout tests run in milliseconds via `TimeProvider` fake clock.
- [ ] Extraction returns a valid `IncidentTicket` on every seeded scenario; `instruction_template_id` always resolves to a catalog entry (selection, never generation).

## Tasks

### ReportingSession saga (D-002, D-007)
- [ ] Saga states: `OPEN → COLLECTING → PROMOTED | EXPIRED | REJECTED | MERGED`; per-reporter session vs shared incident aggregate
- [ ] Session start triggers: intent-gated + evidence-first (photo with no active session)
- [ ] Promotion rule: `(photo OR corroborating reporter) AND location resolved` — sufficiency, not completeness
- [ ] Scheduled messages: 30s pin reminder, challenge expiry (config, ~10 min), `TimeProvider` injected
- [ ] Dedup/merge: 200m haversine (pin) or landmark match + 5min window → MERGED into open incident; late-reporter path (incident already transported → "already handled" reply, no instructions)
- [ ] Session/incident lifecycle decoupling (session may expire while incident dispatches)

### Extraction (D-014, D-003)
- [ ] `IIncidentExtractor` behind `IChatClient`; full `IncidentTicket` structured-output schema incl. cross-modal consistency flag + dispatcher one-liner
- [ ] System prompt: template catalog embedded, severity-errs-high rule, corridor gazetteer for landmark normalization
- [ ] `location_confidence != high` → pin-request flow trigger
- [ ] STT integration (Whisper, D-010): audio → transcript service behind its own interface; transcript stored beside audio ref

### Outbound flows (D-005, D-011)
- [ ] Template store: text (EN/Pidgin/Yoruba) + audio refs per `template_id`, versioned, `approved_by`/`approved_at` fields (clinical sign-off tracking, gate G3)
- [ ] `SendMicroInstruction` / `RequestLocationPin` / `SendStatusUpdate` semantic commands + local-channel handlers
- [ ] Instruction-delivery latency recorded per send (the ≤ 30s median metric, paper objective 4)
- [ ] Placeholder audio files until clinical v1 recorded; swap is data-only

### Timeline summarization
- [ ] `ITimelineSummarizer` for multi-reporter incidents: summary + contradiction surfacing (contradictions flagged, never hidden — R1f); rendered as system annotation

## Added during execution

_(append discovered work here)_
