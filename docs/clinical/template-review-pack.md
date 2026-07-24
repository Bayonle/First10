# First10 — Clinical Micro-Instruction Template Review Pack

**For:** Clinical advisor (with R&A Lead)
**From:** First10 engineering
**Date:** 24 July 2026 · **Version:** 1.1 for review (adds source provenance + Q0)
**Decision needed by:** ~8 August 2026 (soft-launch gate G3 requires an approved library)

---

## 1. What you are approving, and how the system uses it

First10 receives WhatsApp crash reports from bystanders on the Berger–Mowe stretch of
the Lagos–Ibadan Expressway and replies, within seconds, with one short safety message
while FRSC dispatch is arranged.

The safety rules you need to know before reviewing:

1. **The AI never writes safety text.** It only *selects* one template from this
   library, by key (e.g. "this report mentions fire → send the fire card"). The words
   a reporter receives are exactly the words you approve here — verbatim, versioned,
   and logged. This is enforced in code, not by convention.
2. **Unapproved templates cannot be sent.** Every template row carries your sign-off
   (name + date). A row without it is structurally unsendable in the pilot system.
3. **If the AI is unsure, it falls back to the GENERIC card.** So the generic card
   must be safe at *any* crash scene — it is the most important text in this pack.
4. **One message per incident.** A reporter gets a single instruction card per crash
   (currently — see Question Q2 below).
5. Messages are sent in the reporter's detected language: English, Nigerian Pidgin,
   or Yorùbá. All three variants of a card are the "same" instruction — please review
   each language version, not just the English.

**The key review test for every card — the "wrong-card test":** the AI will
occasionally misclassify a scene. Do not only ask *"is this good advice for scenario
X?"* Ask: **"if this card were sent to the WRONG scene, could it cause harm?"**
A card that is helpful in its scenario but dangerous outside it needs rewording or a
stricter trigger.

Style constraints (engineering, for consistency): ≤ ~450 characters, plain words,
short sentences, CAPS only for danger words (NO / DANGER), no medical jargon, nothing
that requires equipment, behaviour reminders rather than treatment procedures
(project paper rule R1d).

---

## 1a. Source provenance — you are verifying, not authoring

These drafts are not invented from scratch. Each card's lines trace to published
doctrine, mapped in the **Provenance** line under each card:

- **NHC §IV** — Nigeria Highway Code, Section IV "First Aid and Casualty Handling"
  (the official national code, produced under FRSC's aegis):
  https://www.highwaycode.com.ng/iv-first-aid-and-casualty-handling.html
- **GRSP** — Global Road Safety Partnership / IFRC, *First Response to Road Crashes:
  First Aid Saves Lives on the Road* (2023):
  https://www.grsproadsafety.org/wp-content/uploads/2023/05/EN_FA_RS_manual_webversion.pdf
- **DEV** — deliberate deviation from or omission of published doctrine, with the
  reason stated. These are the lines that most need your ruling.

Your review is therefore mostly *verification against doctrine you already know*,
plus rulings on the flagged deviations.

### Q0 — THE headline conflict: "remove the casualty from danger" vs "do not move"

NHC §IV's first-listed priority is **"remove the casualty from the danger or remove
the danger from the casualty"**, and it further contemplates trained road users
giving rescue breaths, applying tourniquets, and evacuating victims to hospital
themselves. That guidance is written for trained motorists in a no-ambulance
context.

Our cards deliberately take the opposite default for *untrained, panicking
bystanders receiving an automated message*: **do NOT move anyone; help is being
dispatched** (motivated by spinal-injury risk and the fact that First10's entire
purpose is that help IS coming). This deviation is the single most consequential
clinical decision in this pack. Please rule explicitly:

☐ Confirm the conservative default (do-not-move, with or without a fire exception — see Q1)
☐ Align with NHC §IV (cards should instruct moving casualties from danger)
☐ Other ruling: ______________________________________________

Name: ______________ Date: __________

---

## 2. Proposed taxonomy

Currently implemented: 3 keys. Proposed additions: 3 more (+1 optional). The taxonomy
is deliberately small — every added key makes the AI's selection job harder and adds
rows to the wrong-card matrix. Please strike, merge, or add scenarios as you see fit.

| Key | Trigger (what makes the AI pick it) | Status |
|---|---|---|
| `rta_generic` | Default / any uncertainty | exists — review text |
| `rta_fire` | Fire, smoke, fuel + ignition, tanker involvement | exists — review text |
| `rta_okada` | Motorcycle (okada) involved | exists — review text |
| `rta_trapped` | Person(s) trapped/pinned in or under a vehicle | proposed |
| `rta_pedestrian` | Pedestrian knocked down, victim on the roadway | proposed |
| `rta_spill_no_fire` | Fuel/chemical spill, no fire (yet) | proposed |
| `rta_multiple` | Many casualties (bus, multi-vehicle) | optional — see Q4 |

---

## 3. Templates for review

Each card: trigger rule, draft text (EN / Pidgin / Yorùbá), wrong-card analysis, and
a sign-off block. **All drafts below are engineering placeholders written from the
project paper's conservative defaults — they carry no clinical authority until you
amend and sign them.**

---

### 3.1 `rta_generic` — the default card (most important)

**Trigger:** any road crash report where no other card clearly applies, and every
case where the AI is uncertain.

> **EN:** Help is being arranged. While you wait: keep yourself safe away from
> traffic. Do NOT move anyone who cannot move themselves. If someone is bleeding,
> press firmly on the wound with cloth. Do not give food or water.
>
> **Pidgin:** We dey arrange help. As you dey wait: stand for safe place, no enter
> road. NO move anybody wey no fit move by himself. If person dey bleed, press the
> wound well well with cloth. No give am food or water.
>
> **Yorùbá:** A ń ṣètò ìrànlọ́wọ́. Bí o ṣe ń dúró: dúró sí ibi àìléwu kúrò ní ojú
> ọ̀nà. MÁ ṣí ẹnikẹ́ni tí kò lè mira ní ibi kan. Bí ẹnikan bá ń ṣàn ẹ̀jẹ̀, fi aṣọ tẹ̀
> ẹ́ mọ́ ojú ọgbẹ́. Má fún un ní oúnjẹ tàbí omi.

**Wrong-card analysis:** This card is sent to every ambiguous scene, so it must be
harmless everywhere. Points to check: "do not move anyone" at a scene where a vehicle
is about to burn — is the blanket rule still right, or does it need "unless there is
immediate danger like fire"? "Press firmly on the wound" — acceptable without
glove/HIV caveat? "No food or water" — keep?

**Provenance:** "do not give food or water" — NHC §IV verbatim rule. "Press firmly on the wound with cloth" — NHC §IV (direct pressure) + GRSP. "Do NOT move anyone" — **DEV**: inverts NHC §IV's remove-from-danger priority (see Q0). "Keep yourself safe away from traffic" — GRSP scene-safety first principle.

**Sign-off `rta_generic`:** ☐ Approved as-is ☐ Approved as amended
Name: ______________ Date: __________ Amendments: ______________________________

---

### 3.2 `rta_fire` — vehicle fire / imminent fire

**Trigger:** report mentions fire, smoke, or fuel with ignition risk; tanker crashes.

> **EN:** DANGER: stay far from the vehicle — fire and fuel can explode. Move others
> back at least 50 steps. Do not try to fight the fire. Help is being arranged.
>
> **Pidgin:** DANGER: comot far from the motor — fire and fuel fit explode. Tell
> people make dem shift back like 50 steps. No try quench the fire yourself. We dey
> arrange help.
>
> **Yorùbá:** EWU: jìnnà sí ọkọ̀ náà — iná àti epo lè bú gbàù. Jẹ́ kí àwọn ènìyàn
> padà sẹ́hìn bí ìgbésẹ̀ 50. Má gbìyànjú láti pa iná náà. A ń ṣètò ìrànlọ́wọ́.

**Wrong-card analysis:** if sent to a non-fire scene, the cost is bystanders standing
50 steps from a victim who could have received pressure-on-bleeding help. Acceptable?
Also: no instruction about occupants — deliberately (we never instruct extraction
from a burning vehicle either way; is silence the right clinical choice here, or
does this card need an explicit line?). Is "50 steps" the right distance for a
tanker (vs. much further)? Should tanker get its own card with a larger distance?

**Provenance:** scene evacuation/distance — GRSP (danger-zone principle); NHC §IV supports removing people from danger. "Do not fight the fire" — **DEV**: NHC/GRSP are silent for bystanders; drafted to counter observed corridor behaviour. The 50-step distance — **DEV**: engineering placeholder, no published figure found; please set the number.

**Sign-off `rta_fire`:** ☐ Approved as-is ☐ Approved as amended
Name: ______________ Date: __________ Amendments: ______________________________

---

### 3.3 `rta_okada` — motorcycle crash

**Trigger:** motorcycle/okada involved.

> **EN:** Help is being arranged. Do NOT remove the rider's helmet — it can worsen a
> neck injury. Do not move the rider unless there is immediate danger like fire.
> Press firmly on any bleeding with cloth.
>
> **Pidgin:** We dey arrange help. NO remove the rider helmet — e fit worsen neck
> injury. No move the rider unless danger dey like fire. If blood dey comot, press
> am well with cloth.
>
> **Yorùbá:** A ń ṣètò ìrànlọ́wọ́. MÁ yọ àṣíborí awakọ̀ náà — ó lè mú ìpalára ọrùn
> burú sí i. Má ṣí i ní ibi kan àyàfi bí ewu bá wà bí iná. Fi aṣọ tẹ ojú ẹ̀jẹ̀ mọ́lẹ̀.

**Wrong-card analysis:** low risk — helmet advice is inert when there is no helmet.
Note the corridor reality: many okada riders are unhelmeted; the card should not read
as absurd at a helmetless scene. Suggestion to consider: "If the rider wears a
helmet, do NOT remove it…".

**Provenance:** helmet non-removal — NHC §IV verbatim principle (code's exception: respiratory/circulatory distress; the card omits the exception for untrained bystanders — **DEV**, please confirm). Bleeding pressure — NHC §IV + GRSP.

**Sign-off `rta_okada`:** ☐ Approved as-is ☐ Approved as amended
Name: ______________ Date: __________ Amendments: ______________________________

---

### 3.4 `rta_trapped` — entrapment (PROPOSED)

**Trigger:** person pinned/trapped in or under a vehicle. Corridor-typical: trailer
and tanker underride, cabin crush.

> **EN (draft):** Help is being arranged. Do NOT pull anyone who is trapped — pulling
> can cause worse injury. Stop others from pulling too. Stay where the trapped person
> can hear you and keep talking to them. If you see fire or smoke, move everyone back
> at least 50 steps.
>
> **Pidgin (draft):** We dey arrange help. NO drag anybody wey jam inside — to drag
> am fit worsen the injury. Stop other people make dem no drag am too. Stay near make
> the person hear your voice, dey talk to am. If you see fire or smoke, make
> everybody shift back like 50 steps.
>
> **Yorùbá (draft):** A ń ṣètò ìrànlọ́wọ́. MÁ fa ẹnikẹ́ni tí ó há sínú ọkọ̀ — fífà á
> lè mú ìpalára burú sí i. Dá àwọn mìíràn dúró kí wọ́n má fà á. Dúró ní ibi tí ó ti
> lè gbọ́ ohùn rẹ, máa bá a sọ̀rọ̀. Bí o bá rí iná tàbí èéfín, jẹ́ kí gbogbo ènìyàn
> padà sẹ́hìn bí ìgbésẹ̀ 50.

**Wrong-card analysis:** the primary bystander harm at entrapment scenes is
well-meaning crowds pulling victims out; this card exists to stop that. Sent to the
wrong scene it is mostly inert. **Open clinical question:** the fire-vs-entrapment
conflict — if the vehicle IS burning and someone IS trapped, what should a bystander
be told? The draft dodges by repeating the distance rule; please rule on this
explicitly, it is the hardest scenario on the corridor.

**Provenance:** do-not-pull — extension of NHC §IV's spinal-care rule ("DO NOT LIFT casualty alone", 4–6 trained helpers) to the zero-training case — **DEV** by simplification. Keep-talking reassurance — GRSP (psychological first aid). Crowd restraint — NHC §IV crowd-control rule.

**Sign-off `rta_trapped`:** ☐ Approved as amended ☐ Rejected (key removed)
Name: ______________ Date: __________ Amendments: ______________________________

---

### 3.5 `rta_pedestrian` — pedestrian knocked down (PROPOSED)

**Trigger:** pedestrian struck; victim lying on the carriageway.

> **EN (draft):** Help is being arranged. The biggest danger now is other vehicles.
> Warn oncoming traffic from a distance — use lights, wave cloth, park a vehicle
> with hazard lights before the scene if possible. Do NOT move the injured person
> unless a vehicle is about to reach them. Do not crowd around them.
>
> **Pidgin (draft):** We dey arrange help. The main danger now na other motor. Warn
> motor wey dey come from far — use light, wave cloth, park motor with hazard light
> before the place if e possible. NO move the person wey injure unless motor wan
> reach am. No make crowd gather round am.
>
> **Yorùbá (draft):** A ń ṣètò ìrànlọ́wọ́. Ewu tí ó tóbi jù lọ báyìí ni àwọn ọkọ̀
> mìíràn. Kìlọ̀ fún àwọn ọkọ̀ tí ń bọ̀ láti ọ̀nà jíjìn — lo iná, fì aṣọ, kí ọkọ̀ dúró
> pẹ̀lú iná ìkìlọ̀ ṣáájú ibi ìṣẹ̀lẹ̀ bí ó bá ṣeé ṣe. MÁ ṣí ẹni tí ó fara pa àyàfi bí
> ọkọ̀ bá fẹ́ dé bá a. Má jẹ́ kí ọ̀pọ̀ ènìyàn yí i ká.

**Wrong-card analysis:** this is the only card that conditionally permits moving a
victim ("unless a vehicle is about to reach them"). That exception is clinically and
practically loaded — an untrained bystander judging traffic risk vs. spinal risk.
Please decide: keep the exception, remove it, or reword it. Scene-protection advice
(parking a shield vehicle) also creates a secondary-crash risk of its own — is it in
scope for a bystander message?

**Provenance:** scene protection/warning traffic — GRSP (protect the scene before casualty care). The conditional move exception — **DEV**: a compromise between NHC §IV's remove-from-danger and our do-not-move default; entirely your ruling (see Q0).

**Sign-off `rta_pedestrian`:** ☐ Approved as amended ☐ Rejected (key removed)
Name: ______________ Date: __________ Amendments: ______________________________

---

### 3.6 `rta_spill_no_fire` — fuel spill without fire (PROPOSED)

**Trigger:** fuel/diesel/chemical spill reported, no fire mentioned. Corridor-typical:
tanker skid or valve failure. NOTE: the notorious scenario here is crowds gathering
to scoop fuel.

> **EN (draft):** DANGER: spilled fuel can catch fire at any moment. Move away and
> keep others away — at least 50 steps. No naked flame, no smoking, no generator
> nearby. Do NOT collect the fuel, and warn others not to. Help is being arranged.
>
> **Pidgin (draft):** DANGER: fuel wey pour fit catch fire anytime. Comot from there,
> tell people make dem comot too — like 50 steps. No fire, no cigarette, no gen for
> near there. NO pack the fuel o, warn people make dem no pack am. We dey arrange
> help.
>
> **Yorùbá (draft):** EWU: epo tí ó dà lè gbiná nígbàkigbà. Kúrò níbẹ̀, kí o sì jẹ́
> kí àwọn mìíràn kúrò — bí ìgbésẹ̀ 50. Kò sí iná, kò sí sìgá, kò sí ẹ̀rọ iná
> nítòsí. MÁ kó epo náà, kìlọ̀ fún àwọn mìíràn kí wọ́n má kó o. A ń ṣètò ìrànlọ́wọ́.

**Wrong-card analysis:** mostly inert at wrong scenes. Open questions: (a) should
"do not use your phone near the spill" be included? Engineering note: the
phone-ignites-fuel claim is largely folklore, but if FRSC field doctrine includes
it, consistency may matter more than pedantry — your call. (b) The anti-scooping
line is a crowd-safety essential on this corridor (documented tanker tragedies) but
it may reduce compliance from the reporter themselves — keep, soften, or drop?

**Provenance:** distance + ignition-source discipline — GRSP danger-zone principle. Anti-scooping line — **DEV**: no published first-aid source; drafted from documented Nigerian tanker-spill crowd tragedies. Phone question — see card note.

**Sign-off `rta_spill_no_fire`:** ☐ Approved as amended ☐ Rejected (key removed)
Name: ______________ Date: __________ Amendments: ______________________________

---

### 3.7 `rta_multiple` — mass casualty (OPTIONAL — see Q4)

No draft provided deliberately. Bystander triage guidance ("help the quiet ones
first") is contentious to deliver via automated message. Options: (a) no such key —
mass-casualty scenes get `rta_generic` plus whatever specific hazard card applies;
(b) a minimal card = generic + "tell responders how many people are hurt when they
arrive". Please choose.

**Decision `rta_multiple`:** ☐ No key (use generic) ☐ Minimal card (drafts to follow)
Name: ______________ Date: __________

---

## 4. Policy questions needing a ruling

- **Q1 — The generic card's blanket "do not move".** Keep absolute, or add the
  "unless immediate danger like fire" exception used in the okada card? One
  consistent rule across all cards would be ideal.
- **Q2 — One-shot vs. escalation resend.** Today a reporter gets exactly one card
  per incident. If a scene *changes* (generic card sent, then fire breaks out and
  the AI re-grades), should the system send the danger card as a second message?
  Engineering note: trivial to build, only ever escalating toward danger cards,
  never chatty. Ruling: ☐ one card only ☐ allow danger-escalation resend
- **Q3 — Audio versions.** For low-literacy reporters we want each card as a short
  voice note (same three languages). Do you want to approve the written text first
  and the recordings second, or both together? Who records (voice, dialect)?
- **Q4 — `rta_multiple`** — see 3.7.
- **Q5 — Review cadence.** Templates are versioned; every message logs exactly which
  version was sent. Proposed: any wording change bumps the version and requires
  re-sign-off; a standing review after the pilot's week-2 accuracy meeting. OK?

---

## 5. What happens after you sign

1. Approved texts are loaded with your name + date; the system's clinical gate then
   allows sending. Placeholders are deleted.
2. New keys (trapped / pedestrian / spill) are added to the AI's allowed selection
   list with the trigger rules in §2 — engineering work, ~half a day, after your
   taxonomy ruling.
3. Every message sent during the pilot is traceable to a template id + version in
   this pack; the weekly accuracy review can audit selection quality (was the fire
   card sent to fire scenes?).

*Prepared by First10 engineering. Nothing in this document has clinical authority
until signed. Questions to the R&A Lead.*
