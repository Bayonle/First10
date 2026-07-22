# First10 (AI-powered Bystander Initiated Crash Reporting) — Project Paper

*Prepared by Bayonle Amzat*
*Senior Management Programme, Lagos Business School*
*Date: 12 June 2026*
*Defence deadline: 21 September 2026*

---

## Executive Summary

**Project.** First10 is a WhatsApp-based bystander reporting system that converts crash photos and voice notes into structured emergency dispatch packets — delivered to FRSC dispatch in seconds rather than minutes, and faster and more usable than a free-form 122 phone interview. It is designed to compress the upstream delay in Nigeria's road-trauma response — the gap between an accident occurring and emergency dispatch receiving usable information.

**Prior validation.** A serving FRSC commander has personally validated this approach. Drawing on past command experience, he ran a manual WhatsApp coordination group with community members on his corridor — bystanders dropping crash photos in real time — and found it effective enough to defend as a method. First10 productionises and scales what he proved by hand, with AI doing the triage and routing he had to do manually.

**Positioning.** First10 is a **complementary channel** to the existing FRSC mobile app, not a replacement. The FRSC app serves engaged users who have downloaded it and created accounts; its Eye Witness Report feature is account-gated, English-only, and form-based. First10 serves the occasional bystander — someone who happens to witness a crash, has WhatsApp installed (every Nigerian does), cannot or will not stop to install an app and create an account in the seconds after impact, and may not be literate in written English. Both channels feed the same FRSC dispatch ecosystem.

**Why now.** Nigeria records ~40,000 road-traffic deaths a year — one of the world's highest rates per vehicle. Most preventable deaths occur in the golden hour, and most of that hour is currently wasted before an ambulance is even dispatched. The FRSC has hotlines (122 toll-free, 0700-CALL-FRSC) and a mobile app — but the fact that an experienced commander still chose WhatsApp for fast bystander reporting on his corridor is the empirical signal: existing channels are not closing the gap.

**Three things this system does beyond reporting.** First10 does not just deliver a structured incident packet to dispatch. (1) Within roughly thirty seconds of the bystander's first message, the system replies in vernacular voice with **safety-first micro-instructions** — keep yourself safe, do not move suspected spinal injuries, apply pressure to bleeding — drawn from a clinically pre-approved template library. (2) Once dispatch happens, the system **closes the loop with the reporter**: ambulance dispatched, ambulance arrived, victims transported. (3) When a second or third bystander reports the same incident, the system **stitches their inputs into a live timeline** that the dispatcher sees as it evolves and that the responding crew receives on arrival. Each of these is a small evolution. Together they convert First10 from a *reporting tool* into *the first thing that happens to help the victim* — and they redesign FRSC's failed first-aid-training programme around the principle that actually worked.

**Pilot scope.** Berger-to-Mowe stretch of the Lagos-Ibadan Expressway (~30km), in partnership with the Federal Road Safety Corps (FRSC) Ogun Command, with the commander cited above as the team's named advisor and a clinical advisor (doctor or emergency-medicine nurse) engaged to pre-approve every micro-instruction template.

**Commitment.** ~14 weeks (12 June – 21 September 2026), budget ~₦2.5M **fully self-funded by the team**. Team composition, role assignment and per-member contribution to be determined at kickoff (a leaner alternative budget is also offered in Section 3.4).

**Expected outcome.** A working First10 prototype, FRSC pilot deployment along the corridor, measured reduction in time-to-dispatch versus baseline (target: from ~25 minutes to under 5 minutes), and a panel-ready 10-slide presentation demonstrating viability and replicability.

---

## 1. Scope of the Project

### 1.1 Project vision

To make the first ten seconds after a Nigerian road crash useful — by giving the nearest bystander a frictionless way to deliver a structured emergency report to the right dispatcher, faster than a phone call ever could.

### 1.2 Project mission

Build and pilot, in 14 weeks, a multimodal AI system that:

1. Accepts a photo and a short voice note from any bystander via WhatsApp
2. Extracts incident type, casualty estimate and severity tier from the inputs; derives location from the bystander's spoken voice-cue description (e.g., *"Mowe inbound, near the toll gate"*) with a location-pin follow-up request when the description is ambiguous or missing
3. Delivers a structured one-line ticket to a designated FRSC dispatcher in seconds — under thirty seconds end-to-end for a voice-cue case, under one minute when a location pin is requested
4. **Returns clinically-validated safety-first micro-instructions back to the bystander, in their language, within ~30 seconds of their initial report**
5. **Closes the loop with the reporter as the response unfolds — dispatch, arrival, and transport — without ever exposing victim identity**
6. **Aggregates multiple reports of the same incident into a live timeline visible to the dispatcher and forwarded to the responding crew on arrival**
7. Filters false reports via a multi-reporter confirmation mechanism
8. Preserves victim dignity through automatic server-side face-blurring executed in memory on receipt, before any image is forwarded to FRSC or written to persistent storage

### 1.3 Project objectives

| # | Objective | Measure |
|---|---|---|
| 1 | Reduce time-to-dispatch on the pilot corridor | Baseline ≈ 25 min → target ≤ 5 min |
| 2 | Deliver structured incident data instead of unstructured phone calls | 100% of dispatched reports contain location, type, severity, casualty estimate |
| 3 | Filter false reports without slowing legitimate ones | False-positive rate < 5%; legitimate-report dispatch latency ≤ 30 sec |
| 4 | **Deliver safety-first micro-instructions to every reporter** | ≥ 90% of verified reports receive instructions; 100% of instruction templates clinically pre-approved; median delivery time ≤ 30 sec |
| 5 | **Close the loop with every reporter at dispatch and arrival** | ≥ 80% of verified reports receive at least one outbound status update before the case closes |
| 6 | **Stitch multi-reporter timelines for the dispatcher and responding crew** | 100% of multi-report incidents produce a single timeline; zero contradictions surface uncalled |
| 7 | Demonstrate AI-essentiality and originality to the SMP panel | Score ≥ 60/70 across the seven rubric criteria |
| 8 | Establish a replication path for other Nigerian corridors | At least one written expression of interest from a second FRSC command or partner agency |

### 1.4 In scope

The following are explicitly included in the pilot:

- **Intake channel:** WhatsApp Business API number dedicated to First10
- **AI extraction pipeline:** incident type (vision), casualty estimate (multimodal), severity tier
- **Location handling:** voice-cue extraction from the bystander's spoken description as the default path (works for ~80% of corridor cases where landmark references are sufficient for a corridor-familiar dispatcher); **location-pin fallback** — when the voice description is ambiguous or missing, the system automatically replies in vernacular voice with *"please share your location pin so help can find you"* and waits up to 60 seconds for the WhatsApp location-pin reply. **EXIF-based auto-extraction is not used** — WhatsApp strips EXIF data on inbound images, so it is technically unavailable
- **Vernacular support:** English, Nigerian Pidgin, Yoruba (corridor population)
- **Privacy layer:** **server-side face-blurring**, executed in memory immediately on inbound receipt. Only the blurred image is forwarded to FRSC. Only the blurred image is written to persistent storage. The unblurred image exists transiently in server memory during the blur operation (typically under one second) and is then discarded. No human reviews unblurred images outside an explicitly authorised privacy-incident response procedure. Blurred images are encrypted at rest and access-logged.
- *Note on the architectural change:* the original design intended on-device blurring before upload, but WhatsApp's channel architecture prevents on-device pre-processing of attachments. Server-side blurring with the controls above is the responsible alternative and is fully NDPA-compatible with documented consent, retention and audit policies.
- **Dispatcher dashboard:** simple Streamlit-based dashboard for FRSC Ogun Command duty officers, with structured tickets, basic filtering, and a *live timeline view* for multi-report incidents
- **False-report filter:** rule-based multi-reporter confirmation (two independent reports within 200m and 5 minutes auto-verify; singletons enter a 60-second human-review queue)
- **Safety-first micro-instructions back to the reporter:** clinically pre-approved voice + text replies in English, Pidgin and Yoruba, returned within ~30 seconds of the initial report. Templates are organised by incident type and severity (e.g., RTA non-fire, RTA fire, RTA okada-involved). Every template is signed off by a clinical advisor before use. AI selects which template to send; it does not freely generate clinical advice.
- **Loop closure with the reporter:** automated status updates triggered by dispatcher actions in the FRSC dashboard — *dispatched*, *arrived*, *transported*. Messages are sent as WhatsApp text + voice note. Victim identity is never disclosed to the reporter.
- **Reporter-to-reporter relay:** when two or more bystanders report the same incident within 200m and 5 minutes, the system aggregates their inputs into a single chronological timeline. Subsequent reporters can add updates ("victim now conscious", "crowd forming"). AI summarises the timeline for the dispatcher and produces an on-arrival briefing for the responding crew.
- **Reporter recognition layer:** non-monetary "Citizen First Responder" badge tied to LGA leaderboard, optionally linked to NYSC service-hour credit
- **Bystander training:** structured onboarding for 50 corridor volunteers, including consent capture and brief usage walkthrough
- **NDPA compliance documentation:** consent forms, data-handling protocol, retention policy
- **Pilot evaluation:** measured baseline vs. First10 time-to-dispatch, ticket completeness, accuracy validation, micro-instruction delivery rate, loop-closure rate, relay-timeline integrity
- **Final deliverables:** working system, evaluation report, 10-slide panel deck

### 1.5 Out of scope (explicitly)

To protect the 14-week timeline, the following are out of scope for this pilot:

- Building or replacing FRSC's downstream ambulance dispatch system itself (we deliver to it; we do not replace it)
- Replacing the FRSC mobile app (we explicitly complement it; see Section 1.8)
- **Medical advice beyond the pre-approved safety-first template set** — bystanders are never given clinical decisions; the system delivers behaviour reminders, not diagnoses or treatments
- **Free-form AI generation of clinical content** — the model selects which template to send; it does not write new clinical text
- **Loop-closure messages containing victim identity, medical detail, or outcome beyond transport status**
- **Voice calls back to the bystander** — all communication is WhatsApp text + voice notes, not phone calls
- Hospital coordination after dispatch (this belongs to a separate concept — *GoldenHour*)
- Non-RTA incident types (fires, building collapse, GBV, medical emergencies)
- 24/7 production-grade SLA — this is a piloted system, not a national live service
- Geographic expansion beyond the Berger-to-Mowe corridor
- Native iOS or Android mobile applications (WhatsApp is the deliberate channel)
- Direct integration with Nigeria Police Force, LASEMA or NEMA — only FRSC for the pilot
- Monetisation, commercial pricing, or business-model build-out beyond a paragraph in the panel deck
- Hardware deployment (cameras, sensors, beacons along the corridor)

### 1.6 Success criteria

The project will be considered successful at panel if:

1. The system is **live and operational** during the pilot window (verifiable demo)
2. **≥ 30 verified reports** have been processed end-to-end through First10
3. **Average time-to-dispatch is reduced by ≥ 70%** vs. measured baseline
4. **≥ 90% of verified reports received safety-first micro-instructions** with median delivery ≤ 30 seconds, 100% of templates pre-approved by the clinical advisor
5. **≥ 80% of verified reports received at least one loop-closure status update**
6. **All multi-report incidents produced a coherent dispatcher timeline** with zero unflagged contradictions
7. **FRSC Ogun Command has signed a Letter of Intent** confirming continued use beyond the pilot
8. The panel deck scores **≥ 60/70** on the SMP rubric
9. **No NDPA, ethical, or clinical-content incidents** during the pilot window

### 1.7 Project boundaries

| Boundary | Decision |
|---|---|
| Geographic | Berger to Mowe, Lagos-Ibadan Expressway only (no extension during pilot) |
| Incident types | Road traffic accidents only |
| Partner agencies | FRSC Ogun Command only (fallback: Emergency Response Africa private service if FRSC stalls) |
| User base | ~50 trained corridor bystanders + organic walk-ins via FRSC corridor signage |
| Technology stack | WhatsApp Business API + cloud-hosted AI APIs + simple web dashboard. No native apps, no on-premise. |

### 1.8 Complementarity with the FRSC mobile app

The FRSC mobile app exists and offers an "Eye Witness Report" feature inside an Emergency menu, alongside FRSC 122, NEMA 112 and Army 193 hotline shortcuts. The Eye Witness flow asks the user to create an account (name, email, phone, password), pick from a dropdown of 11 incident types (which mixes Road Traffic Crash with Customer Satisfaction and other categories), type up to 160 characters of description, and attach a photo. The wider app also provides vehicle and licence verification, a black-spots monitor, traffic radio, speed-limiter portal, driving-school standardisation, FAQs and other engaged-user features.

First10 is designed not to replace the FRSC app but to **complement** it by serving a different population of reporters on a different design axis:

| Dimension | FRSC mobile app | First10 |
|---|---|---|
| Target user | Engaged FRSC user who has downloaded the app and signed up | Occasional bystander in a panic moment |
| Account | Required (name, email, phone, password) | None |
| Input modality | Typed text (160 chars) + photo | Voice note + photo |
| Language support | English | English, Nigerian Pidgin, Yoruba (corridor population) |
| Incident classification | User picks from dropdown of 11 categories | AI infers from photo + voice |
| Severity signal | None visible to dispatcher | Multimodal severity tier (high / medium / low) |
| Time to first report | App install + account creation + form fill | Send a WhatsApp photo |
| Single-purpose | No (mixes road crashes with customer-satisfaction reports) | Yes (RTAs only) |

Both channels feed the same FRSC dispatcher. The FRSC app captures the engaged-user channel; First10 captures the everyone-else channel. We commit, in the partnership conversation with FRSC, never to position First10 against the app — only alongside it. This was the explicit framing accepted by the FRSC commander when the team described the project.

---

## 2. Schedule and Timelines

### 2.1 Master timeline

The project runs from **12 June 2026** to **21 September 2026** — 14 working weeks, with the final defence on **24 September 2026**.

Working backwards from the defence date, the project is structured in five phases:

| Phase | Weeks | Dates | Outcome |
|---|---|---|---|
| 1. Mobilisation | W0–W1 | 12 Jun – 21 Jun | Team confirmed, FRSC engagement opened, sprint plan locked |
| 2. Partnership & Design | W2–W3 | 22 Jun – 5 Jul | FRSC LOI signed, NDPA legal review complete, system design approved |
| 3. Build | W4–W7 | 6 Jul – 2 Aug | Core AI pipeline, WhatsApp intake, dispatcher dashboard, false-report filter |
| 4. Pilot | W8–W11 | 3 Aug – 30 Aug | Soft-launch then live pilot on corridor; data collection and iteration |
| 5. Close-out & Defence | W12–W14 | 31 Aug – 21 Sep | Analysis, slide build, dry runs, panel preparation |

### 2.2 Weekly milestones

| Week | Dates | Phase | Key milestone |
|---|---|---|---|
| W0 | 12–14 Jun | Mobilisation | Project kickoff; role assignments confirmed |
| W1 | 15–21 Jun | Mobilisation | In-person follow-up meeting with FRSC commander (named advisor); FRSC Ogun Command meeting #1; topic-submission to Christiana Anukam by 22 Jun deadline; complementarity briefing with FRSC mobile app team |
| W2 | 22–28 Jun | Partnership | FRSC Letter of Intent signed (warmed by commander's advocacy); lawyer engaged for NDPA review; **clinical advisor engaged** |
| W3 | 29 Jun – 5 Jul | Design | System architecture finalised; dashboard wireframes approved by FRSC duty officer; **first draft of micro-instruction template library** |
| W4 | 6–12 Jul | Build | WhatsApp intake operational; basic LLM extraction working in test; **TTS pipeline for vernacular instructions live in test** |
| W5 | 13–19 Jul | Build | Vision classifier integrated; **server-side face-blurring pipeline complete (in-memory, with persistent storage write only of blurred image)**; **voice-cue location extraction operational**; micro-instruction templates reviewed by clinical advisor, first revision |
| W6 | 20–26 Jul | Build | Dispatcher dashboard live; multi-reporter confirmation logic complete; **location-pin fallback flow complete**; loop-closure integration with dispatcher actions complete; relay-timeline backend complete |
| W7 | 27 Jul – 2 Aug | Build | End-to-end integration test; **clinical sign-off on final template set**; **relay timeline end-to-end tested**; bystander training materials finalised |
| W8 | 3–9 Aug | Pilot — soft launch | 10 bystanders trained; controlled-scenario tests on corridor |
| W9 | 10–16 Aug | Pilot — ramp | 50 bystanders trained; full pilot live |
| W10 | 17–23 Aug | Pilot — live | Continued pilot; weekly review with FRSC duty officer |
| W11 | 24–30 Aug | Pilot — live | Final pilot week; data collection closed |
| W12 | 31 Aug – 6 Sep | Close-out | Pilot data analysis; story compilation; testimonials gathered |
| W13 | 7–13 Sep | Slide build | First draft of 10-slide deck; faculty dry-run #1 |
| W14 | 14–20 Sep | Final prep | Final dry runs; slide polish; presentation rehearsal; submission to Christiana on 21 September |
| Defence | 24 Sep | — | Panel presentation |

### 2.3 Critical path

The critical-path activities are those that, if delayed, slip the September 21 deadline directly:

1. **FRSC Letter of Intent** (target: 28 Jun, hard deadline: 5 Jul). Without it, the pilot has no destination. *Owner: Team Leader.*
2. **Clinical advisor engagement** (target: 28 Jun, hard deadline: 5 Jul). No micro-instructions ship without clinical sign-off. *Owner: Team Leader.*
3. **WhatsApp Business API approval and number provisioning** (target: 5 Jul). Typical approval is 5–10 business days. *Owner: Innovation & Solution Lead.*
4. **NDPA legal review sign-off** (target: 19 Jul). Pilot cannot launch without this. *Owner: Research & Analysis Lead.*
5. **Clinical sign-off on final micro-instruction template set** (target: 2 Aug). Templates cannot ship live without it. *Owner: Research & Analysis Lead.*
6. **End-to-end integration test (including instructions, closure, relay)** (target: 2 Aug). Triggers go/no-go for soft launch. *Owner: Innovation & Solution Lead.*
7. **Pilot data collection close** (target: 30 Aug). All metric data must be in by this date for slide-deck analysis. *Owner: Sustainability & Impact Lead.*

### 2.4 Buffer and contingency

The schedule has been deliberately built with two buffer windows:

- **Build phase buffer (W7).** If build slips by up to one week, soft launch can shift to W9 without affecting the live pilot window.
- **Close-out buffer (W12).** If pilot extends by up to one week, slide build absorbs the compression.

If FRSC partnership is delayed past 5 July, the contingency is to switch the primary partner to **Emergency Response Africa (ERA)** or another private ambulance service that interfaces with FRSC by phone — the AI value still demonstrates, and the pilot continues.

---

## 3. Cost Estimates

### 3.1 Budget summary

The total estimated cost of the pilot is **₦2,500,000** (~$1,650 USD at June 2026 rates). The breakdown is below.

| Category | Item | Estimated cost (₦) |
|---|---|---|
| Technology | WhatsApp Business API (4 months pilot scale) | 50,000 |
| Technology | LLM + Vision API budget (extraction + relay summarisation + template selection) | 400,000 |
| Technology | TTS / voice synthesis for vernacular micro-instructions and loop-closure voice notes | 150,000 |
| Technology | Cloud hosting (dispatcher dashboard + backend, 4 months) | 160,000 |
| Technology | Domain, SMS gateway (verification), miscellaneous infra | 50,000 |
| Bystander programme | 40 volunteer stipends @ ₦5,000 × 4 weeks | 800,000 |
| Bystander programme | Bystander training session (venue, materials, refreshments) | 80,000 |
| Bystander programme | Bystander recognition badges, printed materials | 40,000 |
| Field operations | Team travel to corridor — site visits, field validation | 130,000 |
| Field operations | FRSC liaison meetings, partner hospitality | 70,000 |
| Compliance | Legal review (NDPA, decision-support framing) | 200,000 |
| Compliance | Clinical advisor (template authoring, review and sign-off) | 150,000 |
| Communications | Team communications, conference calls, miscellaneous | 50,000 |
| Presentation | Slide design tools, printing, rehearsal venue | 90,000 |
| Contingency | Reserve at ~3% of subtotal | 80,000 |
| **Total** | | **₦2,500,000** |

### 3.2 Funding source

The project is **entirely self-funded** by team members. No external sponsorship, grants, or LBS support funds are sought, in order to keep the project free of donor reporting obligations and to protect the team's freedom to scope and pivot.

| Source | Status | Estimated contribution (₦) |
|---|---|---|
| Team self-funding | Confirmed | 2,500,000 |

The **per-member contribution will be determined by the team at kickoff** — equal split, weighted by role, or staged tranches are all open to negotiation. What is fixed is that the funding source is the team itself.

A leaner alternative budget is presented in Section 3.4 for the team to consider if the full-scope contribution is too steep.

### 3.3 Cost-control mechanisms

Because every Naira is team capital, cost discipline matters more than usual:

1. **Phased spending.** Technology costs released only at start of each phase, not upfront. If a phase slips, downstream spending is held.
2. **Stipend-on-attendance.** Bystander stipends are paid only on attended training + verified weekly check-in, not on enrolment.
3. **Daily API spend cap.** LLM/vision APIs configured with hard daily and monthly spend ceilings.
4. **Weekly burn review.** The Team Leader reviews actual vs. budgeted spend at every weekly stand-up. Variances over ₦25,000 trigger a same-week explanation.
5. **Contingency reserve untouched** until a documented risk event is realised — and replenished by the team if drawn.
6. **No team reimbursement** for personal devices, time, or transport unrelated to corridor work — only direct project costs are pooled.

### 3.4 Leaner alternative budget (~₦1,300,000)

If the team prefers a smaller per-member contribution, the total budget can be cut to **~₦1,300,000** by tightening the bystander programme and accepting a smaller pilot footprint:

| Change vs. full scope | Saving (₦) |
|---|---|
| Reduce bystander cohort: 25 volunteers × 2-week active stipend instead of 40 × 4 weeks | 550,000 |
| Reduce field travel: 4 visits to corridor instead of 8 | 65,000 |
| Reduce legal-review scope: focused NDPA-only consultation, no decision-support framing review | 100,000 |
| Secure clinical advisor pro-bono through medical-school or NMA contact | 100,000 |
| Reduce presentation budget: digital-only, no printed handouts | 60,000 |
| Reduce communications and miscellaneous | 30,000 |
| Reduce partner hospitality | 35,000 |
| Reduce training-session venue cost (use LBS facilities free) | 50,000 |
| Reduce SMS gateway / domain budget | 30,000 |
| Trim TTS / voice synthesis to two languages instead of three | 50,000 |
| Reduce contingency in line with smaller envelope | 40,000 |

The leaner version preserves all technical deliverables — including micro-instructions, loop closure and relay — but accepts a smaller pilot sample size (target ≥ 20 verified reports instead of ≥ 30), a thinner bystander recognition layer, and two vernacular instruction languages instead of three. The clinical advisor secured pro-bono is the largest single saving; if that conversation fails, the team must either return to full scope or pause the clinical-content track. **The leaner envelope is ~₦1,390,000.**

---

## 4. Defined Responsibilities

### 4.1 Roles and ownership

Roles map to the SMP Playbook's suggested structure, with explicit deliverable ownership.

| Role | Holder | Primary deliverables |
|---|---|---|
| **Project Sponsor / Team Leader** | TBD | Overall coordination, FRSC partnership ownership, team funding-contribution administration, weekly stand-up facilitation, final presentation lead |
| **Research & Analysis Lead** | TBD | Situation analysis (PESTLE), baseline data on corridor RTAs, NDPA + regulatory review, pilot data analysis |
| **Sustainability & Impact Lead** | TBD | Impact metrics framework, beneficiary mapping, pilot evaluation design, post-pilot impact report, panel-deck impact slide |
| **Innovation & Solution Lead** | TBD | System architecture, AI pipeline build, WhatsApp integration, dispatcher dashboard, face-blurring, false-report filter, technical demo |
| **Presentation Lead** | TBD | Slide design, narrative arc, visual storytelling, dry runs, demo video, presentation logistics |

Role-to-person mapping (one role per person, multiple roles per person, or roles shared across people) is decided by the team at kickoff. Weekly time commitment per role depends on team size, role combination, and each member's availability — to be agreed at kickoff and tracked through the weekly capacity check (Section 5).

### 4.2 RACI matrix for key deliverables

*R = Responsible, A = Accountable, C = Consulted, I = Informed*

| Deliverable | Team Leader | R&A Lead | Impact Lead | Solution Lead | Presentation Lead |
|---|---|---|---|---|---|
| FRSC partnership LOI | **A/R** | C | I | I | I |
| Clinical advisor engagement | **A/R** | C | I | I | I |
| NDPA compliance documentation | A | **R** | C | C | I |
| Micro-instruction template library (authoring + clinical sign-off) | A | **R** | C | C | I |
| WhatsApp + AI pipeline build | A | I | I | **R** | I |
| TTS / voice-note synthesis pipeline | A | I | I | **R** | I |
| Dispatcher dashboard + relay timeline view | A | I | C | **R** | C |
| Loop-closure integration with dispatcher actions | A | I | C | **R** | I |
| Bystander training materials | A | C | **R** | C | C |
| Pilot evaluation report | A | C | **R** | C | I |
| Final 10-slide deck | A | C | C | C | **R** |
| Team contribution collection & ledger | **R** | C | I | I | I |
| Risk register maintenance | **A/R** | C | C | C | C |
| Weekly stand-ups | **R** | C | C | C | C |

### 4.3 Decision-making authority

| Decision type | Authority |
|---|---|
| Daily build, design, content decisions | Role-owner discretion |
| Cross-role coordination, schedule adjustments within phase | Team Leader |
| Scope changes (adding/removing in-scope items) | Full team consensus + Team Leader sign-off |
| Budget changes > ₦50,000 | Full team consensus (all contributors approve) |
| Drawing on the contingency reserve | Full team consensus |
| Critical-path decisions (e.g., switching partner from FRSC to ERA) | Full team consensus required |
| External commitments (LOIs, public statements) | Team Leader only |

---

## 5. Risk Management

### 5.1 Risk register

Each risk is rated on a 1–5 scale for probability (P) and impact (I). Score = P × I. Risks ≥ 12 are watchlist; ≥ 20 are critical.

| # | Risk | P | I | Score | Mitigation | Owner |
|---|---|---|---|---|---|---|
| R1 | FRSC partnership delayed or denied | 2 | 5 | 10 | Probability lowered: team has a serving FRSC commander as named advisor and warm bridge. Fallback to Emergency Response Africa or private ambulance partner if formal LOI nonetheless slips | Team Leader |
| R1b | FRSC mobile app team perceives First10 as competitive despite our framing | 2 | 3 | 6 | Explicit complementarity briefing in W1; differentiation table (Section 1.8) shared with app team; never position against the app in any external communication | Team Leader |
| R1c | Clinical advisor unavailable or pro-bono falls through | 3 | 4 | 12 | Maintain paid retainer option in budget (₦150K reserved); backup roster of 2 emergency-medicine contacts; if all paths fail, drop micro-instructions from pilot and reframe as Phase 2 — pipeline still ships, with reduced scope | Team Leader |
| R1d | Clinically incorrect micro-instruction reaches a bystander | 1 | 5 | 5 | Template-constrained design (AI never generates clinical text freely); 100% pre-approval by clinical advisor; quarterly content audit; conservative defaults (when in doubt, "wait for help, stay safe, do not move the victim") | R&A Lead |
| R1e | Loop-closure message sent when dispatch did not actually happen | 2 | 3 | 6 | Closure messages triggered only by explicit dispatcher action in dashboard; graceful degradation — if status unknown, no message sent; reporter never receives fabricated updates | Solution Lead |
| R1f | Relay timeline misread by dispatcher (contradictory inputs) | 2 | 3 | 6 | AI summary highlights changes and conflicts visually; dispatcher always sees the underlying individual reports; contradictions are surfaced not hidden | Solution Lead |
| R2 | WhatsApp Business API approval delayed | 2 | 4 | 8 | Apply in W1; fallback to Twilio-backed WhatsApp number; parallel onboarding with provider | Solution Lead |
| R3 | AI mis-classifies incident severity | 3 | 4 | 12 | Human-in-loop dispatcher always decides; AI errs toward higher severity; weekly accuracy review during pilot | Solution Lead |
| R4 | Low bystander adoption on corridor | 2 | 4 | 8 | Probability lowered: an FRSC commander has already empirically demonstrated bystander participation in a manual WhatsApp group on a corridor. Recruit through commuter associations, FRSC corridor signage, NYSC partnership for service-hour credit; aim for over-recruitment (target 50, train 70) | Impact Lead |
| R5 | Privacy / NDPA exposure incident — unblurred images touch our infrastructure | 3 | 5 | **15** | **Architecture changed from on-device to server-side blurring** (WhatsApp constraint). Mitigations: blur runs in-memory on receipt before any forwarding or persistent storage; unblurred image discarded immediately after blur; encrypted-at-rest for blurred images; audit logging of every blur operation; lawyer-reviewed consent and retention policy; no human reviews unblurred images outside authorised privacy-incident response | R&A Lead |
| R5b | Voice-cue location ambiguity (no clear landmark in voice note) | 3 | 2 | 6 | Location-pin fallback automatically requested when AI confidence below threshold; corridor-familiar dispatcher can act on imprecise location for landmark cases; pilot data improves model | Solution Lead |
| R5c | Bystander does not share location pin when asked | 3 | 2 | 6 | Voice-cue extraction provides usable fallback for most cases; corridor is single-stretch so even rough location is actionable; reminder request sent at 30 seconds if no pin received | Solution Lead |
| R6 | Liability if a routed report leads to harm | 2 | 5 | 10 | Decision-support framing in all materials; disclaimers; FRSC owns dispatch decision; lawyer review | R&A Lead |
| R7 | Sample size insufficient for outcome statistics | 4 | 3 | 12 | Pre-commit to process metrics (time-to-dispatch reduction, ticket completeness, accuracy) as primary; outcomes as secondary | Impact Lead |
| R8 | Team capacity conflict with SMP coursework | 4 | 3 | 12 | Realistic hour commitments per role; weekly capacity check at stand-up; surge capacity reserved in W14 | Team Leader |
| R9 | Cost overrun | 3 | 3 | 9 | Phased spending, daily API caps, weekly burn review, contingency reserve | Team Leader |
| R10 | Late-August Lagos rains affect corridor traffic / pilot data quality | 3 | 2 | 6 | Plan for rain-day data normalisation; rain periods are also high-RTA — useful data | Impact Lead |
| R11 | False-report flood (testing or malicious) | 2 | 3 | 6 | Multi-reporter confirmation; rate limits per phone number; abuse-monitoring dashboard | Solution Lead |
| R12 | Cloud / API outage during pilot | 2 | 4 | 8 | Hot-standby on second provider; FRSC fallback to phone for any outage period | Solution Lead |
| R13 | Faculty panel scoring rubric changes | 1 | 3 | 3 | Track Christiana Anukam's communications closely; build to current rubric with margin | Team Leader |
| R14 | Team member unable to honour contribution | 2 | 4 | 8 | Contribution schedule structured in three tranches (kickoff, build, pilot); shortfall absorbed by contingency or scope trim | Team Leader |

### 5.2 Top-five watchlist

The risks that consume the most management attention during the project:

1. **R1c — Clinical advisor availability.** No micro-instructions ship without one. Start outreach in W1; carry both a pro-bono and a paid-retainer path; build a backup roster of two emergency-medicine contacts.
2. **R1 — FRSC partnership.** Materially lower-risk than at project inception because we have a named FRSC commander as advisor and warm bridge. Weekly contact; written communication retained.
3. **R3 — AI mis-classification.** Treated as a quality-management priority (see Section 7); weekly review during pilot.
4. **R7 — Insufficient outcome sample.** Mitigated by pre-committing to process metrics so the panel deck has a defensible story even with modest sample sizes.
5. **R8 — Team capacity.** Hardest to mitigate; honest hour commitments and surge planning are the only real defences.

### 5.3 Risk register cadence and escalation

- **Maintenance:** Updated fortnightly by the Team Leader, reviewed at every other stand-up.
- **Escalation trigger:** Any risk whose score increases by ≥ 5, or any new risk scoring ≥ 12, is raised to a same-day decision call.
- **Issue log:** Realised risks (events) are tracked in a separate issue log with date, action, and resolution.

---

## 6. Stakeholder Expectations

### 6.1 Stakeholder map

| Stakeholder | Type | Interest | Influence |
|---|---|---|---|
| LBS Faculty Panel (judges) | Decision-maker | Project quality vs. rubric | **Very high** |
| Christiana Anukam (SMP admin) | Sponsor/admin | Submission compliance | High |
| FRSC Commander (named advisor) | Champion / advisor | Mission alignment; recognition of his prior validation | **Very high** |
| FRSC Ogun Command | External partner | Operational value, no liability exposure | **Very high** |
| FRSC mobile app team | Internal-to-partner | Confidence that First10 is complementary, not competitive | High |
| Clinical advisor (doctor or emergency-medicine nurse) | External validator | Clear scope (templates only, no live medical decisions); professional protection through scope discipline | **Very high** |
| Bystander volunteers | Beneficiary/user | Recognition, low-friction tool | Medium |
| Crash victims and families | Ultimate beneficiary | Faster response | **Very high** (moral) but no direct voice in project |
| Team members | Internal | Project success, learning, time | High |
| Team members' employers/families | Indirect | Personal time impact | Medium |
| LBS alumni judges | Decision-maker | Originality, business-quality story | High |
| Industry experts on panel | Decision-maker | Practical viability, scalability | High |

### 6.2 Engagement plan

| Stakeholder | Channel | Cadence | Owner |
|---|---|---|---|
| FRSC Commander (advisor) | In-person + WhatsApp + phone | In-person follow-up in W1; fortnightly thereafter; ad-hoc when partnership decisions arise | Team Leader |
| FRSC Ogun Command | In-person + WhatsApp | Weekly (W2–W11); fortnightly thereafter | Team Leader |
| FRSC mobile app team | Email + in-person briefing | One-off complementarity briefing in W1; updates if pilot insights are relevant | Team Leader |
| Clinical advisor | Email + scheduled calls | Engagement call in W2; weekly during template authoring (W3–W6); fortnightly during pilot | R&A Lead |
| Christiana Anukam | Email | Monthly status; submission milestones | Team Leader |
| Bystander volunteers | WhatsApp broadcast + training sessions | Weekly during pilot | Impact Lead |
| Team members | WhatsApp group + Notion + weekly stand-up | Daily async + weekly sync | Team Leader |
| Faculty panel | Through Christiana Anukam | At submission and panel only | Team Leader |
| Lawyer / NDPA reviewer | Email + scheduled calls | At engagement + sign-off | R&A Lead |

### 6.3 Expectations management

- **FRSC** expect operational value with **no added liability** and **no production-SLA commitments**. We frame First10 as decision-support for their duty officers and a complementary channel to their mobile app, not as a replacement for either. Written framing is reviewed at LOI signing.
- **FRSC Commander (advisor)** expects to be credited for the prior validation of the manual WhatsApp approach without being personally exposed in any public-facing material. Quotes used in the deck or evaluation report will be cleared with him in writing first.
- **FRSC mobile app team** expect to be informed before the pilot begins, not surprised after. They are briefed in W1 with the Section 1.8 differentiation table and given a contact for questions.
- **Clinical advisor** expects clear scope — they sign off on templates, they do not provide live medical guidance to individual bystanders. Every public-facing micro-instruction message carries a footer noting it is template-based safety guidance, not personalised medical advice.
- **Faculty panel** expect originality, practicality, and clear measurable impact. We pre-empt the predictable challenge questions (*"What about 122/112?"*, *"What about the FRSC mobile app?"*, *"What if AI mis-classifies?"*, *"Isn't this the first-aid programme that already failed?"*) in the slide deck itself.
- **Bystanders** expect recognition without monetisation. The badge system is positioned from W1 as social-recognition, not income.
- **Team members** expect a realistic time commitment **and a realistic financial commitment**. Both are agreed at kickoff in writing. Hours are protected by the Team Leader; surge weeks and contribution tranches are pre-flagged.

---

## 7. Quality Management

### 7.1 Quality dimensions and criteria

| Dimension | Criterion | Target |
|---|---|---|
| AI classification accuracy | Precision on severity tier (against human-labelled ground truth) | ≥ 85% |
| AI classification accuracy | Recall on RTA detection | ≥ 90% |
| Micro-instruction clinical validity | Templates pre-approved by clinical advisor | 100% |
| Micro-instruction delivery | Median time from report receipt to instruction delivery | ≤ 30 seconds |
| Micro-instruction coverage | Verified reports that receive instructions | ≥ 90% |
| Loop-closure rate | Verified reports that receive ≥ 1 outbound status update | ≥ 80% |
| Loop-closure integrity | Closure messages sent only on explicit dispatcher action | 100% |
| Relay timeline integrity | Contradictions between multiple reports flagged in dispatcher view | 100% |
| Privacy compliance | NDPA review completed and signed off (covers server-side blurring architecture) | 100% pre-pilot |
| Privacy compliance | Server-side face-blurring success rate on test set | ≥ 98% |
| Data protection | No unblurred image written to persistent storage at any point | 100% |
| Data protection | No unblurred image forwarded to FRSC at any point | 100% |
| Data protection | Time from inbound receipt to in-memory blur completion | ≤ 1 second |
| Data protection | No victim identity disclosed to reporter in closure messages | 100% |
| Location handling | Voice-cue location successfully extracted from bystander voice note | ≥ 80% of cases |
| Location handling | Location-pin shared by bystander when requested as fallback | ≥ 70% of fallback cases |
| Bystander training quality | Post-training competency quiz pass rate | ≥ 90% |
| Dispatcher dashboard usability | Time-to-action on a test ticket by FRSC user | ≤ 15 seconds |
| Documentation completeness | All eight project-paper sections current at each weekly review | 100% |
| Code quality | All critical paths covered by integration tests before pilot | 100% |
| Slide deck quality | At least two independent dry-runs with feedback applied | 2 minimum |
| Ethical review | Lawyer sign-off on consent forms and decision-support framing | Pre-pilot |

### 7.2 Quality gates

Five gates must be passed before progress to the next phase:

| Gate | Trigger | Approver |
|---|---|---|
| G1 — Partnership gate | FRSC LOI signed + Clinical Advisor engaged | Team Leader |
| G2 — Design gate | Architecture document + lawyer review + first draft of micro-instruction templates clinically reviewed | Team consensus + Clinical Advisor |
| G3 — Build gate | Integration test passes end-to-end (including instructions, closure, relay) + clinical sign-off on final template set | Solution Lead + Clinical Advisor |
| G4 — Pilot launch gate | Bystander training done + NDPA sign-off + first day data-flow verified + first three micro-instructions delivered successfully | Team Leader |
| G5 — Defence-ready gate | Two dry-runs complete + faculty feedback incorporated | Team Leader + Presentation Lead |

Failing a gate triggers a same-week recovery sprint before progressing.

### 7.3 Review and approval

- **Code reviews:** Every change to the AI pipeline reviewed by the Solution Lead before merge.
- **Document reviews:** Every project document peer-reviewed by at least one other role-holder.
- **Slide deck reviews:** Internal team review (W12), faculty dry-run (W13), final polish review (W14).
- **External review:** Lawyer reviews NDPA + decision-support framing pre-pilot.
- **Bystander feedback:** Continuous via WhatsApp group during pilot; incorporated weekly.

### 7.4 Testing protocols

| Test | When | Method |
|---|---|---|
| AI classification accuracy | W6, W11 | Against held-out dataset of 100 labelled examples |
| Face-blurring effectiveness | W6 | Against 50-image test set |
| Multi-reporter confirmation logic | W7 | Synthetic report streams |
| Micro-instruction delivery latency | W6, W7 | 50 simulated reports; measure median + 95th percentile delivery time |
| Micro-instruction template selection accuracy | W6, W7 | 50 labelled scenarios; clinical advisor reviews instruction chosen for each |
| Loop-closure integration | W7 | Dispatcher triggers each action; verify reporter receives correct status |
| Relay timeline integrity | W7 | 20 multi-report scenarios with planted contradictions; verify dispatcher view flags each |
| End-to-end integration | W7 (G3 gate) | Live simulated scenarios on corridor (full pipeline including instructions, closure, relay) |
| FRSC dispatcher dashboard usability | W7 | Walkthrough with duty officer |
| Load handling | W7 | Simulated spike to 10x normal traffic |
| Bystander onboarding flow | W8 | First 10 trained bystanders |

---

## 8. Progress Tracking

### 8.1 Meeting cadence

| Cadence | Meeting | Duration | Participants |
|---|---|---|---|
| Daily (W14 only) | Async WhatsApp standup | — | Full team |
| Weekly | Team stand-up | 60 min | Full team |
| Fortnightly | Risk register review | 30 min | Team Leader + R&A Lead + Impact Lead |
| Monthly | Sponsor / stakeholder update | 45 min | Team Leader + relevant role-owner |
| Per-phase | Phase-gate review | 90 min | Full team |
| Once at W13 + W14 | Faculty dry-run | 60 min | Full team + invited faculty / alumni reviewer |

### 8.2 Tools

| Tool | Purpose |
|---|---|
| WhatsApp group | Daily async coordination, bystander broadcast |
| Notion (or Google Drive) | Document repository, risk register, decision log |
| Trello (or simple kanban) | Task board for the build phase |
| GitHub | Code repository for the AI pipeline and dashboard |
| Google Sheets | Budget tracker, pilot-evaluation data |
| Calendly | External meeting scheduling (FRSC, sponsors, lawyer) |

### 8.3 Key performance indicators (KPIs)

The KPIs are reviewed at every weekly stand-up:

| KPI | Measure | Target by end of phase |
|---|---|---|
| **Phase progress** | % of phase milestones complete | 100% by phase end |
| **Critical-path health** | Days of slack remaining on critical path | ≥ 5 days throughout |
| **Risk score** | Sum of P×I across all open risks | Trend flat or declining |
| **Budget burn** | Actual spend vs. planned spend | Within ±10% of plan |
| **Stakeholder engagement** | FRSC touchpoints per fortnight | ≥ 2 |
| **Bystander pipeline** | Number trained / target 50 | On-track by W9 |
| **AI accuracy** | Precision on held-out test set | ≥ 85% by W6 |
| **Pilot reports processed** | Verified reports through end-to-end pipeline | ≥ 30 by W11 |
| **Time-to-dispatch reduction** | Mean delta vs. baseline | ≥ 70% reduction by W11 |
| **Micro-instruction coverage** | Verified reports that receive instructions | ≥ 90% by W11 |
| **Micro-instruction delivery latency** | Median time from report receipt | ≤ 30 sec by W11 |
| **Loop-closure rate** | Verified reports that receive ≥ 1 status update | ≥ 80% by W11 |
| **Multi-report incidents handled** | Multi-report incidents with coherent dispatcher timeline | 100% by W11 |

### 8.4 Reporting

| Report | Audience | Cadence | Owner |
|---|---|---|---|
| Weekly status note | Full team | Weekly (Sunday) | Team Leader |
| Fortnightly risk update | Full team | Fortnightly | Team Leader |
| Monthly stakeholder brief | FRSC, Christiana | Monthly | Team Leader |
| Phase-gate report | Full team | At each gate | Phase owner |
| Final pilot evaluation | Faculty, FRSC | Once at W12 | Impact Lead |
| Panel-ready slide deck | Faculty panel | Submitted 21 Sep | Presentation Lead |

### 8.5 Decision log

A single shared document records every cross-team decision: date, decision, rationale, dissent, action items. Maintained by the Team Leader, reviewed at every stand-up. This protects the team from re-litigating settled choices and gives the post-mortem a defensible trail.

---

## Closing note

This paper is a living document. It will be reviewed at every phase gate (G1–G5) and updated to reflect actual progress, realised risks, and pivot decisions. The version submitted for defence on 21 September 2026 will be the historical record of how First10 was built — not the prediction.

The single most important fact in this paper is that **the FRSC Letter of Intent must be in hand by 5 July 2026**. Every other timeline and milestone bends around that one document. Because the team now has a serving FRSC commander as a named advisor and warm bridge into the institution, the LOI conversation is materially de-risked relative to project inception — but it is not automatic, and W1 in-person follow-up is non-negotiable.

The deeper, structural fact is that First10 is no longer a hypothesis. A commander has run the manual version, with citizens, on a Nigerian corridor — and it worked. The bet has changed from *"will this work?"* to *"how fast can we scale what already works?"* The same insight explains why the FRSC's prior attempt to teach citizens first aid failed where this will not: behaviour-change interventions ask too much of bystanders; one-tap reporting asks the least possible.

The three additions in this revision — **micro-instructions back to the bystander, loop-closure with the reporter, and reporter-to-reporter relay** — extend the same principle. The bystander does not have to learn first aid in advance; we deliver the smallest possible set of safety-first guidance in the moment, in their language, drawn from a clinically pre-approved library. The bystander does not have to wonder whether their report mattered; we tell them when help arrived. The dispatcher does not have to choose between one report or several; we stitch them into a single timeline. None of these features ask the user to do more. Each one makes their report deliver more.

The rest is execution.

---

## Appendix: Prior art and validation

This appendix exists to document the empirical and institutional context that shaped the First10 design, in the form the team can cite during the panel defence.

**FRSC commander, manual WhatsApp coordination (corridor pilot, past).** A serving FRSC commander, drawing on past command of a corridor, ran an informal WhatsApp coordination group with community members. Bystanders shared crash photos in real time; he triaged and dispatched manually. He defends the method as effective. First10 productionises this exact approach with AI doing the triage at scale and across languages. His identity is held by the team and will be cited publicly only with written consent.

**FRSC first-aid programme for citizens (past).** The FRSC previously attempted to teach citizens basic first-aid skills so bystanders could intervene at scene before responders arrived. Adoption was low. The FRSC pivoted to the mobile app. The structural insight is that asking citizens to *acquire skills in advance* fails at scale, but asking them to *press one button in the moment* succeeds. First10's micro-instructions feature (Section 1.4) is the failed first-aid programme redesigned around this principle: we deliver the smallest possible safety-first guidance *just in time*, drawn from a clinically pre-approved template library, in the language the bystander already speaks. The bystander acquires no skills in advance; the system meets them where they already are.

**FRSC mobile app.** The FRSC operates a publicly available mobile app. The home screen aggregates vehicle and licence verification, a Black Spots Monitor, traffic radio, driving-school standardisation, FAQs and an Emergency menu. The Emergency menu surfaces hotline shortcuts (FRSC 122, NEMA 112, Nigerian Army 193) and an Eye Witness Report flow. The Eye Witness flow requires an account (name, email, phone, password), then surfaces a dropdown of 11 incident types (Road Traffic Crash, Traffic Jam/Gridlock, Road Obstruction, Road Diversion, Road Closure, Road Maintenance, Animals/Wildlife, Robbery, Patrol Misconduct, Police Misconduct, Customer Satisfaction), a 160-character text field, a photo attachment (10MB max), and a Post button. First10's complementarity strategy with this app is detailed in Section 1.8.

**Existing FRSC hotlines.** Toll-free hotlines exist: 122 (FRSC), 112 (NEMA national emergency), 193 (Nigerian Army), plus 0700-CALL-FRSC and 0700-CALL-NEMA. The empirical signal that these channels are not sufficient on their own is the commander's decision, with the hotlines and the app already in place, to still run a manual WhatsApp coordination group on his corridor.

— *Prepared by Bayonle Amzat for the First10 team*
