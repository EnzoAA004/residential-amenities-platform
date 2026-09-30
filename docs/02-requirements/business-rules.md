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
| RB-016 | Exact event windows, cancellation/refund rules, cleaning/deposit/penalty rules and shared capacity remain configurable/TBD until validated. | TBD |
| RB-017 | For the pilot building, validated price configuration is: Event SUM Base = ARS 50,000; Event Pool AddOn = ARS 10,000; Event Barbecue AddOn = ARS 10,000; SharedLeisure SUM Base = ARS 2,000; ExclusiveLeisure SUM Base = ARS 5,000. | Accepted |

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

## Implementation notes — RB-017 (pilot pricing decision, 2026-09-30)

The product owner validated the pilot building's price amounts on
2026-09-30 (see DEC-013 in `docs/01-discovery/decision-log.md` and the
resolved-questions table in
`docs/01-discovery/assumptions-and-open-questions.md`, OQ-004..OQ-008).

- These are **configurable/effective-dated price rules**, applied through
  the existing admin pricing configuration (`apps/api` price rules /
  `admin-pricing` client page) — not client or domain constants. This does
  not change RB-007 or RB-008: prices remain configuration data, and every
  reservation still stores its own price snapshot.
- The documented totals for an Event with add-ons (e.g. SUM + Pool =
  ARS 60,000) are an arithmetic **derivation** of the three underlying price
  components above; they are not separate price rules to configure.
- This decision does **not** resolve OQ-014. Pool and Barbecue having their
  own price component does not make them independently bookable — per
  RB-002/RB-006, they remain Event add-ons under the current model until
  OQ-014 is answered.
- No `effectiveFromUtc`/activation date was specified by the product owner;
  none is assumed here — applying these amounts as runtime configuration is
  a separate, not-yet-requested action.

## Implementation notes — RB-013 and RB-014 (issue #26)

**RB-013** is implemented for reservations: administrative cancellation is a
state transition to `Cancelled` (with `CancelledAtUtc` and `CancellationReason`),
never a deletion, and payments are never deleted or edited. **RB-014** is
implemented for administrative cancel and reschedule: the actor (from the
session), the timestamp and a mandatory reason (trimmed, at most 500
characters) are recorded on the audit trail in the same transaction, and the
cancellation reason also lives on the reservation.

Not decided by this implementation (still issue #2): what cancelling a
**paid** reservation means financially. The payment stays `Approved`, nothing
is refunded or voided, and the admin read model only flags it for review
(OQ-011). Rescheduling does not reprice: no rule says that moving a booking
changes its price.
