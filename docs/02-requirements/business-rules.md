# Business Rules

These rules are the current v0.1 model. Items marked **TBD** require stakeholder validation.

| ID | Rule | Status |
| --- | --- | --- |
| RB-001 | Event reservations require the SUM as the base resource. | Proposed |
| RB-002 | Pool and barbecue/grill are optional Event inclusions with no separate price of their own (see RB-018) and are never independently bookable outside an Event built on the SUM (see RB-023). | Proposed |
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
| RB-016 | The financial consequence (refund or otherwise) of a disallowed/late Event cancellation remains TBD; any administrative action arising from an incident report (RB-022) is a manual decision, never an automated penalty amount. | TBD |
| RB-017 | ~~For the pilot building, validated price configuration is: Event SUM Base = ARS 50,000; Event Pool AddOn = ARS 10,000; Event Barbecue AddOn = ARS 10,000; SharedLeisure SUM Base = ARS 2,000; ExclusiveLeisure SUM Base = ARS 5,000.~~ | **Superseded by RB-018** |
| RB-018 | For the pilot building, revised validated price configuration is: Event reservation total = ARS 5,000 flat, inclusive of SUM, Pool and Barbecue (Pool/Barbecue add no separate charge); SharedLeisure = ARS 0; ExclusiveLeisure = ARS 0. | Accepted |
| RB-019 | Event time windows for the pilot building: afternoon slot 12:00–18:00; night slot 20:00–03:00 the next day (overnight), building-local time. | Accepted |
| RB-020 | Shared Leisure has no maximum simultaneous capacity and a booking is never rejected for exceeding a participant count; before confirming, the resident must be shown which units already hold a booking in the same period (unit label only — never name, email, user id, membership id or phone). | Accepted |
| RB-021 | A paid Event reservation may be cancelled up to 24 hours before its start; less than 24 hours before start, normal cancellation is not permitted. This rule covers only the cancellation window itself — the financial consequence is RB-016, still TBD. A free Leisure reservation (Shared or Exclusive, RB-018) may always be cancelled with no financial consequence, since no money is involved. | Accepted |
| RB-022 | There is no fixed damage/fine schedule. Residents and administrators can submit an incident/damage report (text plus optional photo/video evidence) to a dedicated reports channel for Administrator review; the system never calculates or charges a penalty automatically. | Accepted |
| RB-023 | The SUM is always the base resource for an Event; Pool and Barbecue can only be included as part of an Event built on the SUM and can never be reserved on their own (finalizes OQ-014). | Accepted |
| RB-024 | Resident accounts are Administrator-created only; there is no public self-service flow to pick a building/unit. An Administrator creates the resident record (building/unit/email), the system emails a single-use verification code, the resident verifies their identity with that code and sets their own initial password, and only then is the account enabled. The Administrator never learns, stores or sends the resident's final password. | Accepted |

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

## Implementation notes — RB-017/RB-018 (pilot pricing, revised 2026-09-30)

RB-017 recorded the product owner's first pilot pricing decision (DEC-013):
per-component Event pricing (SUM + separately-priced Pool/Barbecue add-ons)
plus paid Shared/Exclusive Leisure. **That policy was superseded the same
day** by a second, corrected decision from the product owner (DEC-014),
recorded as RB-018:

- An Event reservation is now a **single flat total of ARS 5,000**,
  inclusive of SUM, Pool and Barbecue — Pool/Barbecue no longer add a
  separate charge on top of a per-component SUM price.
- Shared Leisure and Exclusive Leisure are now **ARS 0** (free), not
  ARS 2,000/ARS 5,000.
- RB-017 is preserved in the table above (struck through, marked
  "Superseded by RB-018") rather than deleted, per the decision-log change
  policy — DEC-013 is not rewritten, DEC-014 supersedes it.
- These remain **configurable/effective-dated price rules** (RB-007) applied
  through the existing admin pricing configuration, not client or domain
  constants; RB-008 (historical price snapshot) is unaffected. No
  `effectiveFromUtc` was specified by the product owner for either decision;
  none is assumed.
- **Zero-cost Leisure is a real technical change, not just a number update**:
  existing price-rule validation, quote, reservation, payment-requirement and
  admin-pricing UI code may assume `amount > 0`. This requires a dedicated
  technical issue (`[P6][Pricing] Support zero-cost Leisure reservations`) —
  it is not simply "set the price to 0" in configuration.

## Implementation notes — RB-013 and RB-014 (issue #26)

**RB-013** is implemented for reservations: administrative cancellation is a
state transition to `Cancelled` (with `CancelledAtUtc` and `CancellationReason`),
never a deletion, and payments are never deleted or edited. **RB-014** is
implemented for administrative cancel and reschedule: the actor (from the
session), the timestamp and a mandatory reason (trimmed, at most 500
characters) are recorded on the audit trail in the same transaction, and the
cancellation reason also lives on the reservation.

Not decided by this implementation (still issue #2, RB-016): what cancelling
a **paid** reservation means financially. The payment stays `Approved`,
nothing is refunded or voided, and the admin read model only flags it for
review. The product owner has since decided the *cancellation window* itself
(RB-021: 24 hours before an Event's start) — this admin-side implementation
predates that decision and does not yet enforce it server-side; see the
follow-up technical issue (`[P6][Reservations] Enforce Event 24-hour
cancellation window`). Rescheduling does not reprice: no rule says that
moving a booking changes its price.
