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
- OQ-003 — Whether full-day reservation belongs in MVP or later.
- OQ-004 — Final base SUM price.
- OQ-005 — Final pool price.
- OQ-006 — Final barbecue/grill price.
- OQ-007 — Final shared leisure price.
- OQ-008 — Final exclusive leisure price.
- OQ-009 — Maximum simultaneous shared leisure capacity.
- OQ-010 — Payment hold duration (candidate discussion: 24/48 hours).
- OQ-011 — Cancellation/refund rules.
- OQ-012 — Cleaning obligations and possible deposit/penalty rules.
- OQ-013 — Who is authorized to confirm cash receipt.
- OQ-014 — Whether pool/barbecue can ever be reserved independently of SUM.
- OQ-015 — Resident onboarding process: invitation, approval or administrator-created accounts.
