# M4 — Privacy, Security & Hardening

**Target:** 6 – 9 Aug 2026 · **Paper mapping:** W8 · **Depends on:** M2, M3 · **External:** NDPA legal review in flight (sign-off blocks G4, not this milestone's build work)
**Goal:** every §7.1 data-protection target enforced structurally and evidenced — this milestone produces the artifacts the lawyer and G4 gate review.

## Exit criteria

- [ ] Blur pipeline passes ≥ 98% on the 50-image test set; receipt-to-blur ≤ 1s (§7.1).
- [ ] Demonstrable by test: no code path persists or transmits unblurred bytes (blur runs in ingest scope; only blurred refs exist downstream) (D-009).
- [ ] Console unreachable without login; media URLs expire; every media access logged with `{who, incident, mediaRef, when}`.
- [ ] Retention job deletes past-window media on schedule; deletions audit-logged.
- [ ] Load test: 10× expected pilot traffic with no dropped envelopes (paper §7.4).

## Tasks

### Blur gate (D-009)
- [ ] Face detection + blur module finalized (detector choice benchmarked on the 50-image set: motion blur, night shots, partial faces, okada helmets)
- [ ] Conservative fallback: detection confidence low → blur larger region / full-frame downgrade, flag for review — never ship a maybe-face
- [ ] Blur audit log per operation (paper §1.4 commitment)
- [ ] Architecture test: no type outside the ingest adapter can reference unblurred bytes

### AuthN/Z
- [ ] OIDC login on the SPA (Entra ID or equivalent); roles: dispatcher, admin
- [ ] API authorization on every console endpoint; SignalR hub auth
- [ ] Local cockpit gate re-verified in Production build config (D-006)

### Audit & retention (D-012)
- [ ] Access-log table + middleware on media issuance and ticket views
- [ ] Retention job as recurring Wolverine scheduled message; window configurable pending lawyer's number; deletion audit rows
- [ ] Voice-note handling documented for lawyer review (voice = personal data, D-013)
- [ ] Encryption at rest verified for blob store + DB backups

### Abuse hardening
- [ ] Webhook signature validation middleware (X-Hub-Signature-256) with replay test
- [ ] Rate limits on all public endpoints; payload size caps on media
- [ ] Secrets in a vault (no connection strings/API keys in config files)

### Reliability
- [ ] Load test at 10× pilot traffic (scenario runner as the generator); queue depth + latency measured
- [ ] Outbox retry behavior verified with the WhatsApp/Meta sender faulted (messages survive process restart)
- [ ] Dead-letter handling + alerting for poisoned envelopes
- [ ] Ops runbook: what the on-call team member does when Meta is down (FRSC falls back to phone per R12 — how we detect and announce)

### Compliance package (inputs to G4)
- [ ] Data-flow diagram (message → blur → storage → console → deletion) for the lawyer
- [ ] Consent-capture flow for trained bystanders reflected in onboarding materials (with Impact Lead)

## Added during execution

_(append discovered work here)_
