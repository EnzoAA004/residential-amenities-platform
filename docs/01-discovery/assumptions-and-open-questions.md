# Assumptions and Open Questions

## Assumptions — v0.1

| ID | Assumption | Status |
| --- | --- | --- |
| A-001 | One pilot building contains units 1A–5B. | Known for pilot |
| A-002 | More than one user may belong to a unit. | Design assumption |
| A-003 | Event reservations are exclusive for the SUM. | To validate |
| A-004 | Shared leisure bookings may overlap with other shared leisure bookings. | To validate |
| A-005 | Prices must be configurable and effective-dated/versioned. | Accepted design direction |
| A-006 | Pending-payment bookings use a configurable hold period. | Accepted design direction |
| A-007 | Source code remains private and the building consumes a hosted service. | Accepted |
| A-008 | Architecture is multi-building-ready without full SaaS administration in MVP. | Accepted |

## Open questions

Two business blockers remain undecided (OQ-010, OQ-013); OQ-003 is deferred,
not blocking MVP scope:

- OQ-003 — Whether full-day reservation belongs in MVP or later. (Deferred)
- OQ-010 — Payment hold duration (candidate discussion: 24/48 hours). Still open — do **not** treat the current 30-minute local/dev default as a decision.
- OQ-013 — Who is authorized to confirm cash receipt. Still open — the system can technically let any Administrator confirm cash today, but that is an implementation default, not the final commercial decision.

## Resolved questions

Questions are recorded here rather than deleted when decided, to preserve the
history of what was asked and when/how it was resolved. See the referenced
`DEC-*` entries in [`decision-log.md`](decision-log.md) and `RB-*` rules in
[`business-rules.md`](../02-requirements/business-rules.md) for full decision
records.

| ID | Original question | Decision | Status |
| --- | --- | --- | --- |
| OQ-001 | Exact afternoon event start/end time. | 12:00–18:00, building-local time. | Decided 2026-09-30 (DEC-014) |
| OQ-002 | Exact night event start/end time. | 20:00–03:00 the next day (overnight slot). Requires technical support for a slot that crosses midnight — see the follow-up implementation issue. | Decided 2026-09-30 (DEC-014) |
| OQ-004 | Final base SUM price. | **Superseded.** Originally ARS 50,000 standalone (DEC-013/RB-017); revised to ARS 5,000 as part of one all-inclusive Event total (DEC-014/RB-018). | Decided 2026-09-30 (DEC-014, supersedes DEC-013) |
| OQ-005 | Final pool price. | **Superseded.** Originally ARS 10,000 add-on (DEC-013/RB-017); revised — Pool no longer adds a separate charge, it is included in the ARS 5,000 Event total (DEC-014/RB-018). | Decided 2026-09-30 (DEC-014, supersedes DEC-013) |
| OQ-006 | Final barbecue/grill price. | **Superseded.** Originally ARS 10,000 add-on (DEC-013/RB-017); revised — Barbecue no longer adds a separate charge, it is included in the ARS 5,000 Event total (DEC-014/RB-018). | Decided 2026-09-30 (DEC-014, supersedes DEC-013) |
| OQ-007 | Final shared leisure price. | **Superseded.** Originally ARS 2,000 (DEC-013/RB-017); revised to ARS 0 — Shared Leisure is free (DEC-014/RB-018). | Decided 2026-09-30 (DEC-014, supersedes DEC-013) |
| OQ-008 | Final exclusive leisure price. | **Superseded.** Originally ARS 5,000 (DEC-013/RB-017); revised to ARS 0 — Exclusive Leisure is free (DEC-014/RB-018). | Decided 2026-09-30 (DEC-014, supersedes DEC-013) |
| OQ-009 | Maximum simultaneous shared leisure capacity. | No maximum capacity — never reject a Shared Leisure booking for exceeding a participant count. Instead, before confirming, the resident must be shown which units are already booked in the same period (unit labels only — never name/email/userId/membershipId/phone). See the follow-up implementation issue if the backend does not yet expose this safely. | Decided 2026-09-30 (DEC-014) |
| OQ-011 | Cancellation/refund rules. | Split into two independent questions, per the product owner's own framing: **(a) cancellation window** — for a paid Event, cancellation is allowed up to 24 hours before the start; less than 24 hours before start, normal cancellation is not permitted. This is now Decided. **(b) refund/financial consequence** of a late/disallowed cancellation is explicitly **not** decided yet — no automatic refund behavior is assumed or implemented. Free Leisure reservations (Shared/Exclusive, now ARS 0) have no financial cancellation policy at all, since there is no money involved. | Decided 2026-09-30 for (a); (b) still requires explicit product clarification when it becomes relevant |
| OQ-012 | Cleaning obligations and possible deposit/penalty rules. | No fixed damage/fine schedule (e.g. "damage X = fine Y") will be built. Instead, the system will support **incident/damage reports** (text + photo/video evidence) submitted by residents or administrators, routed to a dedicated reports/incidents channel for Administrator review. The system never calculates or charges a penalty automatically — any consequence is a manual, out-of-band administrative decision. See the follow-up implementation issue for the report/evidence feature. | Decided 2026-09-30 (DEC-014) |
| OQ-014 | Whether pool/barbecue can ever be reserved independently of SUM. | No. Pool and Barbecue are never independently bookable; the SUM is always the base resource for an Event, and Pool/Barbecue can only be included as part of an Event built on the SUM. | Decided 2026-09-30 (DEC-014) |
| OQ-015 | Resident onboarding process: invitation, approval or administrator-created accounts. | Administrator-created only — there is no public flow where a person freely picks a building/unit. Flow: Administrator creates the resident record (building/unit/email) → the system emails the resident a single-use verification code → the resident verifies their identity with that code and sets their own initial password → the account is enabled. The Administrator never learns, stores or sends the resident's password. See the follow-up implementation issue for invitation/verification/password-reset. | Decided 2026-09-30 (DEC-014) |

OQ-003 remains **Deferred** (not part of MVP scope for now); it is listed
under "Open questions" above rather than here because it has not been
resolved either way.
