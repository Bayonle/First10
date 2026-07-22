# M3 — Dispatch Console

**Target:** 2 – 7 Aug 2026 (parallel with M2 tail) · **Paper mapping:** W7/W8 · **Depends on:** M0 shell; M2 for live data
**Goal:** the FRSC duty officer's working surface — review queue, live incident timeline, loop-closure actions, outcome marking. Usable by a non-technical dispatcher (§7.1: time-to-action ≤ 15s on a test ticket).

## Exit criteria

- [ ] Dispatcher can work a full incident end-to-end from the console: see it arrive → review evidence (play audio, read transcript, view blurred photo) → mark dispatched/arrived/transported → reporter receives each status message.
- [ ] Multi-reporter incident renders one relay timeline with reporter badges, AI summary as a distinct annotation, contradictions visibly flagged.
- [ ] Every loop-closure message demonstrably traces to an explicit dispatcher click (R1e) — verified by test.
- [ ] Outcome marking (real / false / unverifiable) writes the labelled row that feeds the weekly accuracy review.

## Tasks

### Review queue
- [ ] Queue ordered by disposition + severity + age; `AUTO_VERIFY` pre-confirmed at top; `REVIEW` and expired-unverified visible with flags
- [ ] Pending states rendered from saga state ("pin requested 20s ago, awaiting reply")
- [ ] Flood banner (from M1 detector)

### Incident timeline view (D-013)
- [ ] Two-way `TimelineEntry` stream: inbound (text verbatim + language tag, audio player **beside** transcript, blurred images) and outbound (instructions, challenges, closures) inline
- [ ] AI artifacts (ticket fields, severity, summary) as visually distinct system annotations — never interleaved as messages
- [ ] Multi-reporter grouping by session, badges ("Reporter 1/2"), content never merged
- [ ] Media via authenticated endpoint + short-lived signed URLs (issuance access-logged; full audit hardening in M4)

### Dispatcher actions
- [ ] Dispatched / Arrived / Transported buttons → Wolverine command → transactional audit row + outbound status message (outbox enforces R1e)
- [ ] Outcome marking: real / false / unverifiable — one click, writes `outcomes` row
- [ ] Manual disposition override (promote/reject) with reason, audit-logged — dispatcher is the final gate

### Live updates & UX
- [ ] SignalR → TanStack Query invalidation for queue + open timeline
- [ ] Audio playback of AAC/M4A transcodes verified on Chrome, Firefox, Safari
- [ ] On-arrival crew briefing view (printable/shareable summary per §1.4 relay)
- [ ] Usability pass with a non-developer stand-in for the duty officer; measure time-to-action on a test ticket (target ≤ 15s)

### Metrics surface
- [ ] Simple stats page off `dispositions` + `outcomes`: FP rate, time-to-dispatch per ticket, instruction latency, loop-closure rate — the §8.3 KPI numbers for the panel deck

## Added during execution

_(append discovered work here)_
