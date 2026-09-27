# Business Rules

These rules are the current v0.1 model. Items marked **TBD** require stakeholder validation.

| ID | Rule | Status |
| --- | --- | --- |
| RB-001 | Event reservations require the SUM as the base resource. | Proposed |
| RB-002 | Pool and barbecue/grill are independent optional event add-ons with their own price rules. | Proposed |
| RB-003 | Shared-leisure SUM reservations may coexist only with compatible shared-leisure reservations. | Proposed |
| RB-004 | A user creating/joining a shared-leisure window must be informed that the space may be shared. | Proposed |
| RB-005 | Exclusive leisure blocks incompatible SUM use during its time range. | Proposed |
| RB-006 | Event reservations block incompatible SUM use during the complete configured event window. | Proposed |
| RB-007 | Prices are configuration data, not constants embedded in the client. | Accepted |
| RB-008 | A reservation stores a price snapshot/line-item result so later price changes do not rewrite historical charges. | Accepted |
| RB-009 | A pending-payment reservation may hold required resources only until its configured expiration time. | Accepted |
| RB-010 | Expired unpaid reservations release held resources. | Accepted |
| RB-011 | A Mercado Pago redirect/client callback alone does not authorize final payment confirmation; final state is established by backend verification/provider notification. | Accepted design rule |
| RB-012 | Cash payment is not considered received until an authorized person confirms it. | Proposed |
| RB-013 | Financially relevant reservations/payments are cancelled/voided through state transitions rather than silently hard-deleted. | Accepted |
| RB-014 | Administrative reservation changes must record actor, timestamp and reason where applicable. | Accepted |
| RB-015 | Building/unit membership must be authorized; public selection of an arbitrary unit is not sufficient for account creation. | Accepted |
| RB-016 | Exact event windows, prices, cancellation rules, cleaning rules and shared capacity remain configurable/TBD until validated. | TBD |

## Implementation notes (issue #21)

RB-001, RB-002 and RB-006 remain **Proposed**, not stakeholder-approved —
issue #21 implements them as the current working direction because they are
needed to build Event reservations at all, not because #2 has closed. In
particular:

- RB-001 is enforced by requiring the base resource's `AmenityKind` to be
  `Sum` (never the `"SUM"` name/label).
- RB-002's "independent" add-ons currently means "not a required part of a
  Leisure reservation" — a resident cannot yet book Pool/Barbecue on their
  own outside an Event (that is OQ-014, still open).
- RB-006's exclusivity is implemented for every resource an Event books
  (SUM and each add-on), not only the SUM.
- Event slot windows ("afternoon"/"evening") are seeded as explicit
  placeholders in a configurable `EventSlotDefinition` table, not hardcoded.
  Exact Event slot times remain configurable/TBD pending issue #2.

## Implementation notes (issue #23)

RB-009 and RB-010 are now fully implemented — every reservation is created
as a `Pending` hold with a configured `ExpiresAtUtc`
(`Reservations:Hold:DurationMinutes`), and an automatic, idempotent
expiration mechanism (`ReservationExpirationHostedService`) transitions
past-due holds to `Expired`, at which point they stop blocking resources.
See `docs/04-data/domain-model.md#concurrency-and-holds-issue-23`.

The **duration value itself remains configuration, not a decided business
value** — the 30-minute default is a placeholder for local
development/testing, not the candidate 24/48-hour values under discussion
for OQ-010. This issue does not close OQ-010; it only makes the hold
duration a setting rather than a hardcoded constant, so setting the real
value once #2 answers OQ-010 requires no code change.
