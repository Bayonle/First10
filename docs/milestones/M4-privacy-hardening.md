# M4 — Privacy, Security & Hardening

**Target:** 6 – 9 Aug 2026 · **Paper mapping:** W8 · **Depends on:** M2, M3 · **External:** NDPA legal review in flight (sign-off blocks G4, not this milestone's build work)
**Goal:** every §7.1 data-protection target enforced structurally and evidenced — this milestone produces the artifacts the lawyer and G4 gate review.

## Exit criteria

- [~] Blur pipeline: receipt-to-blur ≤ 1s ✅ (580ms warm on a 52-face group photo). ≥98% gate: harness built (`BlurBenchmarkTests`, hard-fails once labelled set exists) — **blocked on the external 50-image set**.
- [x] Demonstrable by test: no code path persists or transmits unblurred bytes — architecture test scans the source tree; `SecureMediaIngest` is the only `SaveAsync` caller (D-009, D-017).
- [x] Console endpoints require the dispatcher role (OIDC bearer; DevAuth in dev); media served only via 5-min signed URLs; ticket views + every URL issuance audit-logged (D-018).
- [x] Retention sweep (self-perpetuating Wolverine scheduled message, chain-id guarded) deletes past-window media + audit rows; verified by tests.
- [x] Load test: 300 messages / 50 reporters at ~10× pilot peak — all accepted, all processed end-to-end (LLM classification live), zero drops.

## Tasks

### Blur gate (D-009)
- [x] Face detection + blur module: UltraFace RFB-640 ONNX in-process + pixelate/Gaussian (D-017). Corridor-set benchmark pending the labelled set (external dep).
- [x] Conservative fallback: maybe-faces (0.35–0.7) blurred with 70% enlarged region; detector failure → full-frame; undecodable bytes refused; missing model → everything full-frame.
- [x] Blur audit log per operation (`blur_audits`: faces, low-confidence regions, min confidence, fallback, duration)
- [x] Architecture test: `IMediaStore.SaveAsync` unreachable outside `SecureMediaIngest` (source-scan test)

### AuthN/Z
- [~] OIDC bearer on the API (configurable authority; refuses to boot unconfigured outside dev); roles dispatcher/admin. SPA login UI → M5 once the Entra tenant exists (external dep).
- [x] API authorization on every console endpoint + SignalR hub; `AuthCoverageTests` forbids unprotected controllers
- [x] Local cockpit gate: [DevelopmentOnly] verified by tests; auth additionally denies everything else in Production without an authority

### Audit & retention (D-012)
- [x] Access-log table (`access_logs`) written on ticket views + signed-URL issuance + retention deletions
- [x] Retention job as recurring Wolverine scheduled message; `Retention:MediaRetentionDays` (30 provisional) pending lawyer's number; deletion audit rows
- [x] Voice-note handling documented for lawyer review (docs/compliance/data-flow.md)
- [ ] Encryption at rest verified for blob store + DB backups — pilot VM provisioning task (M5; MinIO SSE/LUKS + pg backups)

### Abuse hardening
- [x] Webhook signature middleware (X-Hub-Signature-256, constant-time, deny-by-default without secret); replay collapses into (Channel, ExternalMessageId) dedup — tested
- [x] Per-IP rate limiter (600/min fixed window) + 20MB Kestrel body cap + 15MB media upload cap (reporter-level limits remain Stage 0's)
- [~] Secrets: all committed configs hold empty placeholders; dev uses user-secrets; production refuses to boot without Auth/SigningKey config. Actual vault choice = deploy-time decision (M5).

### Reliability
- [x] Load test at 10× pilot traffic: 300 msgs / 50 reporters, all 202-accepted, 100% processed through live-LLM triage
- [ ] Outbox retry behavior verified with the WhatsApp/Meta sender faulted (messages survive process restart)
- [x] Dead-letter handling + alerting: `/api/system/dead-letters` + hard red console banner whenever count > 0 (D-008: never silent); replay = engineer runbook item
- [ ] Ops runbook: what the on-call team member does when Meta is down (FRSC falls back to phone per R12 — how we detect and announce)

### Compliance package (inputs to G4)
- [x] Data-flow diagram + per-category handling table + open questions (docs/compliance/data-flow.md)
- [ ] Consent-capture flow for trained bystanders reflected in onboarding materials (with Impact Lead)

## Added during execution

- **Load test caught silent report loss (the exact D-008 failure mode):** a NEW reporter's rapid messages race on conversation creation while the first message's transaction is held open by a multi-second LLM classification call; the 2.6s retry ladder expired inside the race window and 96/300 messages dead-lettered — invisible except in logs. Fixed: retry ladder extended to ~21s cumulative (past LLM p99), dead-letter count surfaced as a hard red console banner. Earlier wild-human rapid-fire tests missed this because they ran on the instant heuristic classifier. Structural follow-up for M5: split ingest into fast-persist → async-classify so the conversation row commits before any LLM call.
- RFB-320 missed 1–2 small faces on a dense 50-face group photo → upgraded default detector to UltraFace RFB-640 (52/52, 580ms warm); RFB-320 kept as low-latency fallback.
- Wolverine cannot be used before host start: retention kickoff moved from Program startup into a `RetentionBootstrapper` hosted service gated on ApplicationStarted.
- **Video evidence (D-019):** the gate's content-type policy made explicit and closed — the old passthrough assumed non-image = no faces, which video breaks; only the stores' separate whitelist stopped it (a coincidence, not a control). Now: video → up to 4 frames extracted in-scope (ffmpeg), each frame blurred individually, blurred frames composited into one contact-sheet JPEG; raw video never persisted; undecodable video refused. Verified live end-to-end: mp4 → blurred 2×2 sheet in the console, extractor read the sheet ("crowd gathered"), photo-mismatch correctly capped the synthetic footage at Review. ffmpeg is now a deployment prerequisite.
- **Dispatcher-action audit hardened:** the acting officer is now the authenticated principal (was client-supplied with a "duty-officer" default — spoofable once OIDC is live), and every dispatch/arrive/transport/outcome writes a queryable `AccessKind.DispatcherAction` row (who, ticket, action detail) in the same transaction as the state change, complementing the human-readable timeline notes. Invalid/ignored transitions write nothing.
- Outbox retry with the Meta sender faulted + ops runbook → moved to M5 with the WhatsApp adapter itself (no Meta sender exists yet to fault).
