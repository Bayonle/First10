# First10 — Engineering Docs

Companion to [`first10-project-paper.md`](../first10-project-paper.md). The paper owns scope, budget, risk, and governance; these docs own the technical build.

## Contents

| Doc | Purpose |
|---|---|
| [DECISIONS.md](DECISIONS.md) | Technical decision log (per §8.5 of the project paper — single shared document, reviewed at stand-ups) |
| [milestones/M0-foundations.md](milestones/M0-foundations.md) | Solution scaffold, local channel provider, walking skeleton |
| [milestones/M1-intake-triage.md](milestones/M1-intake-triage.md) | Channel abstraction, triage funnel, anti-spam |
| [milestones/M2-session-extraction.md](milestones/M2-session-extraction.md) | Reporting-session saga, AI extraction, micro-instructions |
| [milestones/M3-dispatch-console.md](milestones/M3-dispatch-console.md) | React console: timeline, review queue, loop closure |
| [milestones/M4-privacy-hardening.md](milestones/M4-privacy-hardening.md) | Blur gate, auth, audit, retention, load tests |
| [milestones/M5-pilot-readiness.md](milestones/M5-pilot-readiness.md) | WhatsApp adapter live, §7.4 test protocols, G3/G4 gates |

## Schedule reality (as of 22 Jul 2026)

The paper's build phase is W4–W7 (6 Jul – 2 Aug). We are entering build in W6 with the stack freshly decided, so the original weekly milestones (§2.2) no longer map 1:1. The plan below re-anchors on today and consumes the paper's two sanctioned buffers (§2.4): soft launch shifts from W8 to W9, and close-out absorbs any residual slip.

| Milestone | Target window | Paper mapping |
|---|---|---|
| M0 Foundations | 22 – 25 Jul | W6 |
| M1 Intake & Triage | 26 – 29 Jul | W6/W7 |
| M2 Session & Extraction | 30 Jul – 3 Aug | W7 |
| M3 Dispatch Console | 2 – 7 Aug (parallel track) | W7/W8 |
| M4 Privacy & Hardening | 6 – 9 Aug | W8 |
| M5 Pilot Readiness (G3 + G4) | 8 – 11 Aug | W8/W9 |
| Soft launch (10 bystanders) | 12 – 16 Aug | W9 (buffer used) |
| Full pilot live | 17 – 30 Aug | W10–W11 (unchanged) |
| Pilot data close | **30 Aug — hard** | W11 (unchanged) |

Non-negotiables that do not move: pilot data close 30 Aug, submission 21 Sep, defence 24 Sep.

**Standing external dependencies (start now, not at their milestone):**

1. **Meta Business verification + WhatsApp Cloud API application** — 5–10 business days; blocks M5, not M0–M4 (local provider decouples development). Owner: Solution Lead. Status: ☐ not started
2. **Clinical template library v1** — blocks M2's instruction content (pipeline can build against placeholder templates). Owner: R&A Lead + clinical advisor.
3. **Labelled test set** (~100 crash photos + voice notes, corridor languages) — blocks M5's §7.4 accuracy protocols. Source via FRSC commander advisor. Owner: R&A Lead.
4. **NDPA legal review** — blocks G4/soft launch. Owner: R&A Lead.

## Working agreements

- Milestone docs are living checklists — check items off in place, add discovered work under "Added during execution."
- A milestone is done when its **exit criteria** pass, not when its tasks are checked.
- Decisions that change scope or architecture get a new entry in DECISIONS.md before the code lands.
