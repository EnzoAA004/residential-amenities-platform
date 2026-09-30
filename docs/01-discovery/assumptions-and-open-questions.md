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

- OQ-001 — Exact afternoon event start/end time.
- OQ-002 — Exact night event start/end time.
- OQ-003 — Whether full-day reservation belongs in MVP or later. (Deferred)
- OQ-009 — Maximum simultaneous shared leisure capacity.
- OQ-010 — Payment hold duration (candidate discussion: 24/48 hours).
- OQ-011 — Cancellation/refund rules.
- OQ-012 — Cleaning obligations and possible deposit/penalty rules.
- OQ-013 — Who is authorized to confirm cash receipt.
- OQ-014 — Whether pool/barbecue can ever be reserved independently of SUM. Not answered by the OQ-004..OQ-008 pricing decision below: having its own price component does not by itself make an amenity independently bookable.
- OQ-015 — Resident onboarding process: invitation, approval or administrator-created accounts.

## Resolved questions

OQ-004 through OQ-008 were originally raised here as open pricing questions.
They have since been decided by the product owner for the pilot building and
are recorded below rather than deleted, to preserve the history of what was
asked and when it was resolved. See DEC-013 in
[`decision-log.md`](decision-log.md) and RB-017 in
[`business-rules.md`](../02-requirements/business-rules.md) for the full
decision record.

| ID | Decision | Status |
| --- | --- | --- |
| OQ-004 | Pilot base SUM (Event) price: ARS 50,000. | Decided 2026-09-30 |
| OQ-005 | Pilot Pool Event add-on: ARS 10,000. | Decided 2026-09-30 |
| OQ-006 | Pilot Barbecue Event add-on: ARS 10,000. | Decided 2026-09-30 |
| OQ-007 | Pilot Shared Leisure price: ARS 2,000. | Decided 2026-09-30 |
| OQ-008 | Pilot Exclusive Leisure price: ARS 5,000. | Decided 2026-09-30 |

These are the validated *amounts*, configured as price-rule data per RB-007
(never hardcoded in application code); they do not decide OQ-014, which
remains open above.
