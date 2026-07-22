# First10 — AI-powered bystander crash reporting (SMP pilot)

WhatsApp-based bystander reporting for the Lagos–Ibadan Expressway (Berger–Mowe), piloted with FRSC Ogun Command. Converts crash photos + voice notes into structured dispatch tickets; replies with clinically pre-approved safety instructions; closes the loop with reporters; merges multi-reporter incidents into one timeline. Hard deadlines: pilot data close **30 Aug 2026**, defence **24 Sep 2026**.

## Start every session here

1. Read `docs/STATUS.md` — current milestone, last completed work, next task, blockers.
2. Read the current milestone doc in `docs/milestones/` — exit criteria + unchecked tasks.
3. Skim `docs/DECISIONS.md` before proposing any architecture change — most "should we X?" questions are already decided there.

## End every working session by

1. Checking off completed tasks in the milestone doc (in the same commit as the work).
2. Updating `docs/STATUS.md` (takes 2 minutes — this is the handoff to the next session).
3. Appending to `docs/DECISIONS.md` if anything was decided or reversed.
4. Adding newly discovered work under the milestone's "Added during execution".

## Stack (decided — see docs/DECISIONS.md for rationale)

- **Backend:** ASP.NET Core, controllers (not minimal APIs). Projects: `First10.Api` / `First10.Application` / `First10.Domain` / `First10.Infrastructure`.
- **Messaging:** WolverineFX — commands/queries, `ReportingSession` saga, scheduled messages for ALL timers (no cron), durable outbox over EF Core + Postgres.
- **AI:** everything behind `IChatClient` / Microsoft.Extensions.AI; pilot provider is OpenAI (`Microsoft.Extensions.AI.OpenAI`). Structured-output schemas in `First10.Domain` are the provider-neutral contract. STT = Whisper, separate interface.
- **Frontend:** React + Vite + TypeScript, TanStack Router + Query, SignalR push → Query invalidation.
- **Channels:** WhatsApp, Telegram, and a Local dev provider behind `InboundChannelMessage` normalization + per-channel outbound senders. Identity = `(Channel, ExternalUserId)`; dedup = `(Channel, ExternalMessageId)`.

## Non-negotiable invariants (enforced by design — do not regress)

- **Unblurred images never leave the ingest scope** — blur is local + in-memory before persistence, before any external API call, before the console (D-009).
- **AI selects clinical templates by id; it never generates clinical text** (D-011, D-014).
- **AI never dispatches** — every disposition ends at a human; loop-closure messages only fire from an explicit dispatcher action via the outbox (D-008, R1e).
- **Text-only reports are never silently dropped** — worst case is review-queue + challenge (D-008).
- **Severity errs high** when the model is uncertain (R3).
- **The Local provider must be unreachable in production builds** (D-006).
- Meta media URLs expire in ~5 min — download synchronously in the ingest adapter, never lazily (D-012).

## Key docs

| Doc | Content |
|---|---|
| `first10-project-paper.md` | Scope, budget, risks, stakeholders, quality gates (the SMP document of record) |
| `docs/README.md` | Milestone index, re-anchored schedule, external dependencies |
| `docs/DECISIONS.md` | Technical decision log D-001…D-014 |
| `docs/STATUS.md` | Live project state — the cross-session handoff |
| `docs/milestones/M0…M5` | Work checklists with exit criteria |
