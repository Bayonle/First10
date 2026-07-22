# M5 — Pilot Readiness (Gates G3 + G4)

**Target:** 8 – 11 Aug 2026 · **Paper mapping:** W8/W9 · **Depends on:** M1–M4 · **External (all must land here):** WhatsApp Cloud API approval, clinical sign-off on final template set, NDPA sign-off, labelled test set, 10 trained soft-launch bystanders
**Goal:** the real channel live, §7.4 test protocols executed and passing, go/no-go evidence assembled for G3 (build gate) and G4 (pilot-launch gate). Soft launch follows 12–16 Aug.

## Exit criteria (mirror paper §7.2 gates)

**G3 — build gate**
- [ ] End-to-end integration test on the WhatsApp channel with real phones on the corridor: report → instructions → console → loop closure, including relay and challenge paths.
- [ ] Clinical advisor sign-off recorded on the final template set (text + recorded audio, all three languages).
- [ ] §7.4 protocol results at target (table below).

**G4 — pilot-launch gate**
- [ ] NDPA legal sign-off in hand.
- [ ] 10 soft-launch bystanders trained, consent captured, numbers seeded as high-trust.
- [ ] First-day data-flow verification plan written (who watches what on day one).
- [ ] First three micro-instructions delivered successfully in a controlled corridor scenario.

## Tasks

### WhatsApp channel adapter (D-005, D-012)
- [ ] Webhook verification handshake + signature middleware in production config
- [ ] Media download within URL expiry window; retry/alert on failure
- [ ] Outbound sender: session messages vs approved template messages (24h rule); templates registered with Meta **early — approval takes days**
- [ ] Native location-request message for the pin flow
- [ ] Phone-number allowlist mode for soft launch (trained bystanders + team only), removable flag for W10 ramp

### §7.4 test protocol execution

| Test (paper §7.4) | Target | Result |
|---|---|---|
| AI classification accuracy — 100 labelled examples | severity precision ≥ 85%, RTA recall ≥ 90% | ☐ |
| Face-blur — 50-image set | ≥ 98% | ☐ (from M4, re-run on final build) |
| Multi-reporter confirmation — synthetic streams | 100% correct merge/verify | ☐ |
| Instruction latency — 50 simulated reports | median ≤ 30s, p95 recorded | ☐ |
| Template selection — 50 labelled scenarios | clinical advisor reviews each selection | ☐ |
| Loop-closure integration — each dispatcher action | correct status to reporter, 100% | ☐ |
| Relay integrity — 20 scenarios, planted contradictions | 100% flagged in console | ☐ |
| Load — 10× spike | no dropped envelopes | ☐ (from M4, re-run) |
| STT accuracy spot-check — real corridor voice notes (D-010) | usable transcripts per language; decision recorded | ☐ |

- [ ] Scenario-runner scripts checked in for every row above (repeatable for the W11 re-run)

### Baseline measurement (closes the paper's open gap)
- [ ] Agree baseline time-to-dispatch measurement method with FRSC duty officer (current 122/phone flow) and record it **before** soft launch — the ~25min claim needs evidence behind the panel deck's headline delta
- [ ] Console metrics page shows live time-to-dispatch delta vs recorded baseline

### Launch operations
- [ ] Production deploy environment (hosting, TLS, monitoring, alerting) with deploy runbook
- [ ] Error alerting to team WhatsApp/email (dead letters, webhook failures, API errors)
- [ ] Bystander training session support: QR/wa.me deep link to the First10 number, walkthrough script (with Impact Lead)
- [ ] Go/no-go review meeting: walk both gate checklists with the team; record decision + dissent in DECISIONS.md

## Post-M5 (pilot window, for visibility)

- Soft launch 12–16 Aug (10 bystanders, controlled scenarios) → ramp to 50 (W10) → live through 30 Aug data close
- Weekly: accuracy review vs dispatcher-marked outcomes (R3), burn review, FRSC duty-officer check-in
- W11: re-run classification accuracy protocol on pilot data (paper §7.4)

## Added during execution

_(append discovered work here)_
