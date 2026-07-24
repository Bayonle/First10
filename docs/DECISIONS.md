# First10 — Technical Decision Log

Per project paper §8.5: single shared document, every cross-team decision, reviewed at stand-ups. Newest entries at the bottom. Format: context → decision → consequences. Revisit only with full-team consensus (paper §4.3).

---

## D-001 — Backend: .NET / ASP.NET Core with controllers

**Date:** 2026-07-22 · **Status:** Accepted

**Context.** Team skill set and preference for a typed, batteries-included backend. Original sketch was Python/FastAPI.

**Decision.** ASP.NET Core (latest LTS), classic controllers (not minimal APIs). Solution layout: `First10.Api` / `First10.Application` / `First10.Domain` / `First10.Infrastructure`.

**Consequences.** Python-ecosystem components (face blur, STT) are consumed as services/libraries from .NET rather than in-process Python. Controllers stay thin — all real work goes through Wolverine (D-002).

---

## D-002 — WolverineFX for messaging, sagas, and outbox

**Date:** 2026-07-22 · **Status:** Accepted

**Context.** The pipeline is inherently asynchronous (webhook ack must be fast; Meta retries deliveries), stateful per conversation, and full of timers (pin reminder 30s, challenge expiry, retention). Outbound sends must be atomic with state changes.

**Decision.** WolverineFX for commands/queries, the `ReportingSession` saga, scheduled (delayed) messages for all timeouts, and the durable outbox over EF Core + Postgres.

**Consequences.**
- Ticket-state change + outbound WhatsApp send + timeline event are one transaction. This *structurally enforces* risk mitigation R1e (no loop-closure message without a committed dispatcher action) and gives the ≤30s micro-instruction latency metric an audit trail for free.
- No cron jobs anywhere — every timer is a durable scheduled message delivered back to the saga.
- Webhook controllers do nothing but validate, normalize, publish, and return 200.

---

## D-003 — AI behind Microsoft.Extensions.AI; pilot provider = OpenAI

**Date:** 2026-07-22 · **Status:** Accepted

**Context.** We want declarative AI calls, provider portability, and DI-native wiring rather than hand-rolled HTTP.

**Decision.** All AI touchpoints (intent gate, extraction, timeline summarizer) are injected services behind `IChatClient` / Microsoft.Extensions.AI abstractions. Pilot runs on the `Microsoft.Extensions.AI.OpenAI` provider.

**Consequences.** Provider swap is a DI registration change. The provider-neutral contract is the set of structured-output schemas (`IntentResult`, `IncidentTicket`, timeline summary) — these are versioned in `First10.Domain` and must not leak provider-specific types. STT (D-010) is a separate service, not part of this abstraction.

---

## D-004 — Frontend: React + Vite + TypeScript + TanStack Router/Query

**Date:** 2026-07-22 · **Status:** Accepted · **Supersedes:** project paper §1.4 "Streamlit-based dashboard"

**Context.** The dispatcher console needs a live multi-reporter timeline, audio playback, review queue, and one-click loop-closure actions — beyond comfortable Streamlit territory. Same stack also hosts the local dev cockpit (D-006).

**Decision.** Single SPA: React + Vite + TS, TanStack Router (routes) and TanStack Query (server state), SignalR for live push (server events invalidate Query caches).

**Consequences.** Project paper needs a scope-note update at the next phase gate (§1.4, §1.7 technology stack row). Console requires real auth before pilot (D-013, M4).

---

## D-005 — Channel abstraction: WhatsApp + Telegram + Local

**Date:** 2026-07-22 · **Status:** Accepted

**Context.** WhatsApp is the pilot channel, but coupling the core to it makes iteration slow (Meta approval, webhooks, real phones) and forecloses Telegram as an R2 fallback.

**Decision.** Channels touch the system at exactly two edges:
- **Inbound:** per-channel adapters normalize to `InboundChannelMessage` (channel, external user id, external message id, kind, text, media ref, location, timestamp). Adapters own channel dirty work: Meta's ~5-minute media URL expiry, Telegram `getFile`, signature validation, blur + transcode before the envelope is published.
- **Outbound:** core publishes semantic commands (`SendMicroInstruction`, `RequestLocationPin`, `SendStatusUpdate`); per-channel Wolverine handlers translate, branching on a `ChannelCapabilities` record (voice support, native location request, 24h-template rule, session window).

Identity rules: `Conversation` is keyed by `(Channel, ExternalUserId)` — never bare phone number. Dedup key is `(Channel, ExternalMessageId)` with a unique index (all channels redeliver).

**Consequences.** WhatsApp's 24h/template logic lives only in the WhatsApp sender. Telegram is a ~2-day adapter whenever wanted. Core pipeline is testable with zero external dependencies via D-006.

---

## D-006 — Local channel provider as a dev cockpit

**Date:** 2026-07-22 · **Status:** Accepted

**Context.** WhatsApp approval is on the critical path (paper §2.3 item 3); development must not block on it. §7.4 test protocols require synthetic report streams anyway.

**Decision.** A `Local` channel implementation with a chat UI in the SPA (dev-only route) feeding the identical ingest pipeline. Features: multiple sender personas (exercises multi-reporter dedup), image upload + browser `MediaRecorder` voice notes (real audio through the STT path), map-click location pins, rendering of outbound messages (full loop in two browser tabs), and a **scenario runner** for scripted sequences with controlled timing. `TimeProvider` injected into saga timeout scheduling so timeout tests run in milliseconds.

**Consequences.** Entire pipeline buildable and demoable before Meta approval. Scenario runner *is* the W7/§7.4 test harness (synthetic streams, planted-contradiction timelines). **Hard gate required:** local provider route + controller must be unreachable in production builds — an exposed fake-injection endpoint is a report-forging vector (R11).

---

## D-007 — Provisional ticket at session start, not session end

**Date:** 2026-07-22 · **Status:** Accepted

**Context.** A "collect everything, then create ticket" flow optimizes for completeness; First10 optimizes for the golden hour. Sessions can take minutes to resolve (challenges, pins).

**Decision.** Session start ⇒ provisional `IncidentTicket` exists immediately. Every subsequent message enriches it. "Session complete" is a terminal state transition (`PROMOTED` / `EXPIRED` / `REJECTED` / `MERGED`), not ticket creation. Promotion rule is **sufficiency, not completeness**: `(photo OR corroborating reporter) AND location resolved`.

Session starts on: (a) intent gate says `new_incident`, or (b) **evidence-first** — any photo from a number with no active session, regardless of intent classification. Sessions are per-reporter; incidents are shared aggregates — dedup (200m/5min) merges sessions into one incident, and session/incident lifecycles are decoupled (a session may expire while its incident dispatches; late reporters after transport get "already handled" instead of instructions).

**Consequences.** Dispatcher watches tickets enrich live. Timer-driven expiry never silently kills a ticket — expired-unverified tickets remain visible; the human makes the kill call.

---

## D-008 — Triage funnel with evidence-gated dispositions

**Date:** 2026-07-22 · **Status:** Accepted

**Context.** Need to accept text-only reports, resist spam/pranks/floods, and hold FP < 5% without slowing legitimate reports (paper objective 3) — no single classifier does all three.

**Decision.** Four-stage funnel; each stage cheap enough to run on everything:
- **Stage 0 (deterministic, no AI):** per-number rate limits, blocklist, conversation-state routing, perceptual-hash check against previously seen + known-viral crash images (WhatsApp strips EXIF; pHash is the freshness proxy).
- **Stage 1 (cheap LLM intent call):** `new_incident | incident_update | question | greeting_or_test | spam_or_abuse` + language + confidence. Prompt biases toward `new_incident` when uncertain and carries Pidgin/Yoruba few-shot examples.
- **Stage 2 (disposition):** `AUTO_VERIFY | FAST_TRACK | REVIEW | CHALLENGE | DROP`. **Evidence level caps disposition:** photo+voice → auto-verify eligible; photo → fast-track; voice → review; **text-only → review + challenge, never auto-dispatch, never silent drop.**
- **Stage 3:** full multimodal extraction, only for surviving reports.

CHALLENGE = elicitation reply ("send a photo and your location pin") — converts real text-only reporters into full-evidence reports and starves spammers, while the provisional ticket stays visible in the review queue flagged "awaiting evidence."

Additional signals: corridor geofence (flag, don't drop), cross-modal consistency field in the extraction schema, reporter reputation (trained volunteers start high-trust; dispatcher-confirmed false reports stick), flood detection capping dispositions at REVIEW (R11).

**Consequences.** AI never dispatches — every disposition ends at a human, so system FP is bounded by dispatcher review, not model accuracy. The asymmetry is codified: a false positive costs a dispatcher seconds; a false negative costs a life.

---

## D-009 — Face blur: local, in-memory, before anything else

**Date:** 2026-07-22 · **Status:** Accepted

**Context.** Paper §1.4 mandates server-side in-memory blurring, no unblurred persistence, no unblurred forwarding. Sending unblurred images to *any* external API (including AI providers) would put a third-party processor in the NDPA story.

**Decision.** Blur runs locally in the ingest adapter (OpenCV-class detector + Gaussian blur), in a single function scope. Only blurred bytes are ever persisted, sent to the AI provider, or shown in the console. Unblurred bytes die with the handler scope.

**Consequences.** No role, debug flag, or admin view can ever render an unblurred face — the data doesn't exist. §7.1 gates: ≥98% blur success on the 50-image test set (M4), ≤1s receipt-to-blur.

---

## D-010 — STT: Whisper for the pilot

**Date:** 2026-07-22 · **Status:** Accepted · **Revisit:** after M5 accuracy check

**Context.** Corridor languages are English, Nigerian Pidgin, Yoruba. Whisper large-v3 handles English + Yoruba; Pidgin transcribes serviceably as English-adjacent text that the LLM normalizes downstream.

**Decision.** Whisper API for pilot STT. Dispatcher console always pairs transcript with playable original audio (D-013), so STT errors are recoverable by a human who speaks the language.

**Consequences.** Mandatory accuracy check against real corridor voice notes before G3 (M5). If Pidgin/Yoruba accuracy is unacceptable, evaluate Nigerian-language ASR vendors — the STT service is behind its own interface, swap is contained.

---

## D-011 — Micro-instruction audio: pre-recorded human voice, not TTS

**Date:** 2026-07-22 · **Status:** Accepted · **Supersedes:** paper §3.1 TTS line (₦150K)

**Context.** The clinical template library is small (~20–30 templates × 3 languages) and frozen after clinical sign-off. TTS adds cost, a quality risk on vernacular, and a re-review burden.

**Decision.** Record all approved templates once with native speakers. AI selects a `template_id`; the system plays stored audio + sends stored text. TTS only if dynamic audio content is ever needed (none in pilot scope).

**Consequences.** ₦150K TTS budget line largely freed (flag at next budget review). Re-recording required only when the clinical advisor revises a template. Aligns exactly with the paper's "AI selects, never generates clinical content" rule.

---

## D-012 — Media pipeline: immediate download, transcode at ingest, signed URLs

**Date:** 2026-07-22 · **Status:** Accepted

**Context.** Meta media URLs expire in ~5 minutes; WhatsApp voice notes are OGG/Opus, which Safari won't play; media is sensitive personal data.

**Decision.**
- Download media synchronously in the ingest adapter (never lazily from a queue).
- Audio: store original OGG + transcode to AAC/M4A once at ingest.
- Storage: encrypted blob storage; DB stores refs only (`TimelineEntry` stream per incident).
- Serving: authenticated media endpoint issuing short-lived signed URLs; every issuance access-logged `{who, incident, mediaRef, when}`.

**Consequences.** A copied console link dies in minutes. Access log satisfies the paper's "encrypted at rest and access-logged" commitment. Retention job (M4) hard-deletes past the lawyer-set window, deletions themselves audit-logged.

---

## D-013 — Console shows the full conversation; blurred media only; AI clearly marked

**Date:** 2026-07-22 · **Status:** Accepted

**Context.** Dispatcher trust requires checking AI output against raw inputs; §1.4 promises the dispatcher sees underlying individual reports.

**Decision.** The console renders the complete two-way timeline per incident: text verbatim, voice as playable audio **beside** its transcript, images (blurred only), and outbound sends (instructions, challenges, closures) inline. AI artifacts (ticket, severity, timeline summary) render as visually distinct system annotations, never interleaved as messages (R1f). Multi-reporter incidents group by session with reporter badges; content never merged. Pending states ("pin requested 20s ago") render from saga state. Console requires real login (OIDC) before any pilot traffic.

**Consequences.** Voice = personal data under NDPA: same encryption/access-log/retention treatment as images; explicitly in the lawyer's review scope.

---

## D-014 — Extraction: single multimodal structured-output call

**Date:** 2026-07-22 · **Status:** Accepted

**Context.** Classification, severity, casualty estimate, location cues, language, and template selection all derive from the same photo + transcript.

**Decision.** One structured-output call returns the full `IncidentTicket` schema (incident type, severity tier, casualty estimate, location cue + confidence, language, `instruction_template_id`, cross-modal consistency flag, one-line dispatcher summary). Two prompt rules: **template selection, never generation** of clinical content; **severity errs high** when uncertain between tiers (R3).

**Consequences.** Structured output guarantees a well-formed ticket — no parse failures in the hot path. `location_confidence != high` triggers the pin-request flow. Schema is the provider-neutral contract per D-003.

---

## D-015 — .NET Aspire for local orchestration

**Date:** 2026-07-22 · **Status:** Accepted · **Supersedes:** M0's docker-compose plan

**Context.** M0 originally planned docker-compose for Postgres + API + SPA. Mid-build, the team chose .NET Aspire instead.

**Decision.** `First10.AppHost` orchestrates local development: Postgres container (persistent data volume), the API project, and the Vite dev server (`AddNpmApp`), with service discovery injected into the Vite proxy config. One command boots everything: `dotnet run --project src/First10.AppHost`. The Aspire dashboard provides logs/traces across all resources.

**Consequences.** No hand-maintained docker-compose for dev. Blob-storage emulation is added to the AppHost when the first consumer lands (M2 media pipeline), not before. Production deployment packaging is decided separately in M5 (Aspire can generate artifacts, or plain Dockerfiles — defer until hosting target is chosen). CI remains plain `dotnet build/test` + `npm run build` — no Aspire dependency in CI.

---

## D-016 — Object storage: S3-compatible API, MinIO in development

**Date:** 2026-07-22 · **Status:** Accepted

**Context.** M1 needed real media storage for the cockpit's images/voice notes. The original plan deferred blob storage to M2 with an Azure-flavored assumption.

**Decision.** `IMediaStore` gets an S3-compatible implementation (AWS SDK, `ForcePathStyle`) used against a **MinIO container** orchestrated by Aspire in development. Production targets any S3-compatible endpoint — including self-hosted MinIO on the pilot VM, which fits the self-funded budget. The filesystem store remains as the no-dependency fallback (bare `dotnet run`, tests).

**Consequences.** No cloud-vendor lock-in on media. M4's encryption-at-rest and signed-URL serving layer on top of the same interface. MinIO console (dev) doubles as a media inspection tool. **License note:** ImageSharp pinned to 3.1.x (Six Labors Split License — free at this project's scale; v4 requires a paid license key at build time). Revisit if the project's revenue status ever changes.

---

## D-017 — Face blur: UltraFace ONNX in-process, conservative by construction

**Date:** 2026-07-23 · **Status:** Accepted

**Context.** D-009 requires faces blurred before persistence, before any external API, before the console. Cloud vision APIs are structurally ineligible (they would see unblurred bytes). M4 needed a detector that runs in-process.

**Decision.** UltraFace (RFB-640 variant, ~1.5MB ONNX, Microsoft.ML.OnnxRuntime) runs inside the ingest scope; detected regions get irreversible pixelate+Gaussian. Conservative ladder: low-confidence detections (0.35–0.7) are blurred with a 70% enlarged region; detector failure on a decodable image ⇒ full-frame blur + flag; undecodable bytes are refused outright. Missing model file ⇒ every image full-frame blurred (deployment fault stays a visible degradation, never a privacy hole). `SecureMediaIngest` is the only legal caller of `IMediaStore.SaveAsync`, pinned by an architecture test that scans the source tree. Every blur writes an audit row (faces, confidence, fallback, duration).

**Consequences.** RFB-640 found 52/52 faces on a dense group photo in ~580ms warm (within the §7.1 ≤1s budget); RFB-320 stays vendored as a low-latency fallback via `Blur:ModelPath`. pHash is computed on blurred images — consistent, so reuse detection is unaffected. The extraction LLM sees blurred scenes by construction. The ≥98% gate is measured by `BlurBenchmarkTests` the moment the labelled 50-image set lands (external dep).

---

## D-018 — Evidence access: signed URLs minted per timeline fetch, append-only audit

**Date:** 2026-07-23 · **Status:** Accepted

**Context.** §7.1 requires every media access logged `{who, incident, mediaRef, when}` and media URLs that expire. A separate "get me a URL" endpoint per asset would add a round-trip per image and blur the audit trail.

**Decision.** Fetching a ticket timeline mints 5-minute HMAC-signed URLs directly into the DTOs; that issuance moment writes the audit rows (one TicketViewed + one MediaUrlIssued per asset, tied to the authenticated user). The serve endpoint validates signature+expiry and nothing else — unsigned/expired/tampered ⇒ 403. Signing key comes from config (vault in pilot); production boot fails without it. Retention sweep (self-perpetuating Wolverine scheduled message with a chain-id guard against duplicate chains across deploys) deletes media past the window and writes MediaDeleted audit rows; audit tables are never swept. Console auth: OIDC bearer (Auth:Authority) with dispatcher/admin roles; Development runs a DevAuth scheme so authorization stays structurally on. An `AuthCoverageTests` reflection test forbids unprotected controllers; `/api/webhooks` is deny-by-default until Meta:AppSecret exists.

**Consequences.** The SPA needed no auth plumbing yet (signed URLs arrive in DTOs; DevAuth covers dev). SPA login UI + real Entra tenant land with M5 once the tenant exists. Access-log volume is bounded by console usage (pilot scale trivial).

---

## D-019 — Video evidence: blurred contact sheet at the edge, raw video never persisted

**Date:** 2026-07-23 · **Status:** Accepted

**Context.** Bystanders on WhatsApp send videos of crash scenes at least as often as photos. Full-video face blurring (per-frame tracking, re-encoding) is a hard D-009 problem, and the previous behavior — refusal by way of the media stores' extension whitelist — both lost real evidence and left the gate's safety resting on a coincidence: `SecureMediaIngest` passed all non-image types through unblurred, and only the stores' separate whitelist stopped video.

**Decision.** The gate's content-type policy is now explicit and closed: image → blur; whitelisted audio → passthrough; **video → up to 4 evenly-spaced frames extracted in-scope (ffmpeg, temp-spooled and deleted before return), each frame face-blurred individually at full detector resolution, blurred frames composited into ONE contact-sheet JPEG, only the sheet persisted**; anything else refused. A video that cannot be decoded is refused outright — never stored raw. One mediaRef out means the entire downstream pipeline (evidence ceilings, pHash, multimodal extraction, console, signed URLs, retention) treats video evidence exactly like a photo, with zero schema changes. ffmpeg is a deployment prerequisite (`brew install ffmpeg` dev, `apt install ffmpeg` pilot VM); without it videos are refused, not leaked.

**Consequences.** The dispatcher sees a 2×2 sheet of moments spanning the clip; the extraction LLM gets multi-moment context in a single image. The raw clip's motion/audio is deliberately sacrificed — accepting it would mean storing unblurrable faces. The M5 WhatsApp adapter maps inbound video messages to this path (and must ack the reporter so a failed video still elicits "abeg snap picture" rather than silence, per D-008).

---

## D-020 — Console design: "incident command" dark center with corridor map

**Date:** 2026-07-23 · **Status:** Accepted (supersedes the "dispatch manifest" visual direction)

**Context.** A command-center design concept (dark three-pane: queue · live map · detail) was adopted wholesale by the team leader over the paper-manifest aesthetic. Reviewed against project reality first: multi-agency responder-unit dispatch with ETAs was cut (no unit tracking exists — the pilot dispatches via FRSC radio relay), and prominent reporter phone-number display was cut (data minimization; timelines keep anonymous reporter badges).

**Decision.** Dark-only "incident command" console: 400px live queue (search, severity filters, KPI strip: active / high-sev / unassigned / oldest-wait), Leaflet corridor map (CARTO dark tiles, severity-colored markers, selection synced queue↔map with fly-to), and a detail panel (reported statement, media grid, one-click audited severity re-grade, progress rail received→triaged→dispatched→arrived→resolved, dispatch/override/outcome actions, digest/contradictions/briefing, full message timeline). Token names from the manifest system were kept (`paper/ink/sev/warn/ok/act`) with dark values swapped in, so all pages restyled without rewrites. New backend surface: `RegradeSeverity` command (audited, no-op when unchanged) and ticket list DTO now carries lat/lng + dispatch timestamps.

**Consequences.** Theme toggle and day theme are gone (dark-only). Map tiles are an external CDN dependency (CARTO) — acceptable for the console (not reporter-facing); revisit if the control room's connectivity is poor. The queue no longer nags about theme parity; `.impeccable.md` records the new direction for future design work.

---

## D-021 — Landmark-inferred locations: the corridor's addressing system, below pin trust

**Date:** 2026-07-24 · **Status:** Accepted

**Context.** Reporters name places, not coordinates ("accident for Kara bridge"), and many never manage a pin mid-panic. FRSC dispatches by landmark anyway. Before this, a pinless text+photo report sat un-promotable at "awaiting location pin" forever.

**Decision.** A curated corridor gazetteer (11 landmarks Berger→Mowe with aliases, coordinates, radii — PROVISIONAL until the FRSC waypoint session verifies them). Extraction SELECTS a `landmark_key` from the closed list (same safety pattern as clinical templates — never invented coordinates; unknown output → null; heuristic fallback does alias matching). An inferred location: counts as located (promotion + no pin-nagging), carries `LocationSource.LandmarkInferred` + the key, renders visibly approximate (amber ≈ pill; hollow dashed map marker + 1km halo). Hard trust boundaries: a real pin ALWAYS replaces the inference (never the reverse), and corroboration merges consider pin-sourced locations only — a landmark spans ~1km and Kara can host two distinct crashes at once; merging on approximate geography would fuse them. Human dispatchers merge judgment-wise.

**Consequences.** "Tanker for Ibafo" + photo is now a promotable, mappable, dispatchable ticket with zero pins. Verified live: LLM selected `ibafo`, ticket promoted, dashed marker on-corridor. Alias discipline matters ("punch" alone would match pidgin violence — aliases must be unambiguous in crash prose; tested). Gazetteer verification joins the FRSC waypoint meeting agenda.

---

## Template for new entries

```
## D-0XX — Title
**Date:** · **Status:** Proposed | Accepted | Superseded by D-0YY
**Context.** …
**Decision.** …
**Consequences.** …
```
