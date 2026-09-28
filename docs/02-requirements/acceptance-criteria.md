# MVP Acceptance Criteria

These scenarios are intentionally technology-agnostic. They define observable product behavior, not implementation details.

Items marked **BLOCKED/TBD** depend on stakeholder decisions in issue #2.

## AC-01 — Authentication and membership

**Given** a registered user with an active resident membership  
**When** the user authenticates successfully  
**Then** the backend identifies the user and their authorized building/unit context.

**And given** a user without permission for an admin operation  
**When** they invoke that operation  
**Then** the backend returns an authorization failure regardless of what role/fields the client sends.

Related: RF-001, RF-002, RNF-002, RB-015 — issues #9, #10.

---

## AC-02 — Availability query

**Given** a configured amenity and booking window  
**When** a resident requests availability for a date/range  
**Then** the backend returns availability after considering configuration, maintenance blocks and existing incompatible reservations.

Related: RF-003, RF-020 — issue #19.

Issue #19 delivers the configuration/maintenance portion of this AC (structural
availability: operating windows minus unavailable periods), per the Amenities &
Availability module boundary (`docs/03-architecture/module-boundaries.md`). The
"existing incompatible reservations" portion is completed once Reservations
(#20/#21/#23) can query booked resources.

---

## AC-03 — Shared leisure

**Given** a SUM time range containing a compatible shared-leisure reservation  
**When** another resident requests shared leisure in the same compatible range  
**Then** the system may accept the reservation only after clearly indicating that the space is shared.

**BLOCKED/TBD:** maximum shared capacity and final compatibility policy require issue #2.
Until answered, issue #20 accepts any number of compatible shared-leisure
reservations for the same resource/time; the reservation response always
identifies the use type as shared so a client can inform the user.

Related: RF-004, RB-003, RB-004 — issue #20.

---

## AC-04 — Exclusive leisure

**Given** an available SUM time range  
**When** a resident creates an exclusive-leisure reservation  
**Then** incompatible bookings cannot be accepted for the same resource/time.

**Given** an incompatible booking already exists  
**When** another exclusive/shared-incompatible request is submitted  
**Then** the request is rejected without creating a valid reservation.

Related: RF-005, RF-010, RB-005 — issues #20, #23.

Issue #20 delivered ordinary transactional conflict detection (overlap +
exclusivity checked within the same request/transaction). Issue #23 closes
the remaining race between two truly concurrent incompatible requests with a
PostgreSQL advisory lock — see AC-12 and
`docs/04-data/domain-model.md#concurrency-and-holds-issue-23`. RNF-005 is
now considered satisfied.

---

## AC-05 — Event + amenities

**Given** a configured event slot  
**When** a resident requests an event with valid optional resources  
**Then** the system reserves the SUM plus selected resources as one booking context and delegates price calculation to Pricing.

**BLOCKED/TBD:** exact event windows, full-day behavior and whether add-ons can ever be independent require issue #2.

Related: RF-006, RF-007, RB-001, RB-002, RB-006 — issue #21.

Issue #21 delivers this as a configured `EventSlotDefinition` match (not an
arbitrary time range that merely falls inside the SUM's general
availability), with Pool/Barbecue as the only permitted add-on kinds and
every one of the Event's resources — SUM and add-ons alike — reserved
exclusively for the whole window. "Independent" pool/barbecue bookings
outside an Event (OQ-014) are still not implemented.
Exact Event slot times remain configurable/TBD pending issue #2.

Related: RF-006, RF-007, RB-001, RB-002, RB-006 — issue #21.

---

## AC-06 — Pricing snapshot

**Given** active pricing rules  
**When** the backend quotes and accepts a reservation  
**Then** the authoritative price is calculated server-side and stored as historical reservation price lines.

**When** an administrator later changes future pricing  
**Then** the historical amount of the existing reservation remains unchanged.

Related: RF-008, RF-009, RF-019, RB-007, RB-008 — issue #22.

---

## AC-07 — Payment hold and expiration

**Given** a reservation requiring payment  
**When** the reservation enters pending-payment state  
**Then** its required resources are held until the configured expiration.

**When** that expiration passes without valid payment confirmation  
**Then** the reservation expires and its resources become available again.

**BLOCKED/TBD:** exact hold duration requires issue #2.

Related: RF-011, RF-012, RB-009, RB-010 — issue #23.

Implemented: every reservation is created `Pending` (the hold) with a
configurable `ExpiresAtUtc` (`Reservations:Hold:DurationMinutes`); it blocks
resources exactly like `Confirmed` while active and stops the instant it is
past due. `ReservationExpirationHostedService` releases expired holds
automatically (idempotent bulk `UPDATE`, safe under concurrent execution) —
see `docs/04-data/domain-model.md#concurrency-and-holds-issue-23`. The
30-minute default duration is an explicit placeholder, not the real value
under discussion for OQ-010 (24/48 hours).

Since issue #24 a `Pending` hold can also move to `Confirmed`, but only via a
server-verified payment (AC-08); the hold expiring first always wins over a
late payment (the reservation stays `Expired`).

---

## AC-08 — Mercado Pago

**Given** a payable reservation  
**When** a resident initiates online payment  
**Then** private Mercado Pago credentials remain server-side.

**When** the browser/client returns from the payment provider  
**Then** that client return alone does not mark the booking as paid.

**When** a valid trusted provider confirmation is processed  
**Then** the Payment state is updated and Reservations receives the payment outcome exactly once from a business-effect perspective.

**When** the same provider event is delivered again  
**Then** processing is idempotent and produces no duplicate payment/confirmation effect.

Related: RF-013, RF-014, RNF-003, RNF-006, RB-011 — issue #24.

Implemented (issue #24): `POST /api/reservations/{id}/payments/mercadopago`
creates a Checkout Pro order through the Orders API (amount/currency from the
`ReservationPriceLines` snapshot only); `POST /api/webhooks/mercadopago`
verifies `x-signature`, re-fetches the order server-side and validates order
id / `external_reference` / amount / currency before anything changes. Only an
order that is `processed` + `accredited` approves a payment, and only then does
Payments call Reservations' `ConfirmPaidReservationAsync`. Duplicate
deliveries are no-ops. Payment-vs-expiration is serialized with a row lock;
an approval after expiry never revives the reservation and is recorded as
`ApprovedAfterExpiry` (manual review). Tested with a fake provider — CI never
calls Mercado Pago. Details:
`docs/04-data/domain-model.md#payments-and-mercado-pago-issue-24`.
Refunds are out of scope.

---

## AC-09 — Cash

**Given** a resident chooses cash  
**When** the payment is declared  
**Then** it remains pending confirmation.

**Given** an authorized confirmer/admin receives the cash  
**When** they confirm receipt  
**Then** payment/reservation transition occurs and the action is auditable.

**Given** a resident without confirmation authority  
**When** they attempt to confirm their own cash payment  
**Then** the operation is rejected.

**BLOCKED/TBD:** exact authorized person/process requires issue #2.

Related: RF-015, RF-016, RB-012 — issue #25.

Implemented (issue #25): `POST /api/reservations/{id}/payments/cash` creates a
`Cash` payment in `Pending` (amount/currency from the price snapshot; the
reservation stays `Pending` and its hold is **not** extended).
`POST /api/payments/{id}/cash/confirm` is restricted to `Administrator`
— the **provisional** authorized actor until OQ-013 (issue #2) is answered; a
Resident gets 403. Confirmation approves the payment, records who confirmed
and when on the payment, and asks Reservations to confirm through the same
contract used by Mercado Pago. Repeating it is a no-op that keeps the original
actor/timestamp. If the reservation already expired or was cancelled, the
payment is still `Approved` (the cash was received) with outcome
`ApprovedAfterExpiry` / `ApprovedForCancelledReservation` and
`requiresManualReview`, and the reservation is never revived.
"Auditable" is met at payment level (actor, timestamp, payment, reservation,
amount, outcome); the cross-cutting `AuditLog` remains issue #27.
Details: `docs/04-data/domain-model.md#cash-payments-issue-25`.

---

## AC-10 — Administration and audit

**Given** an authenticated Administrator  
**When** they view reservation/payment details or perform an allowed cancel/reschedule/configuration action  
**Then** the operation passes through the owning module rules rather than bypassing them.

**When** an important administrative/financial transition succeeds  
**Then** an audit record captures the actor, timestamp, target/action and safe relevant metadata.

**When** a financial reservation is cancelled  
**Then** history remains traceable rather than being silently hard-deleted.

Related: RF-017 through RF-021, RB-013, RB-014 — issues #26, #27.

---

## AC-11 — Responsive cross-platform client

**Given** a supported desktop or mobile browser viewport  
**When** a resident uses the core flows  
**Then** the interface remains usable and readable without requiring desktop-only interaction.

**And** the Ionic/Capacitor project remains configured so Android/iOS packaging can be introduced without rewriting the client.

Related: RF-022, RF-023, RNF-008, RNF-009 — issue #7.

---

## AC-12 — Reservation concurrency correctness (RNF-005)

**Given** two truly concurrent requests for incompatible bookings on the
same resource/time
**When** both are submitted at effectively the same moment
**Then** exactly one is confirmed as a hold and the other receives a
conflict response; neither a multi-resource Event nor any of its individual
resources is ever persisted partially.

Related: RF-010, RNF-005 — issue #23.

Implemented via a per-Amenity PostgreSQL advisory lock held for the
duration of the conflict-check-and-insert transaction (see
`docs/04-data/domain-model.md#concurrency-and-holds-issue-23` for why this
was chosen over an exclusion constraint or `Serializable` isolation).
Verified with genuinely concurrent HTTP requests against a real PostgreSQL
database, for both single-resource Leisure and multi-resource Event
bookings.

## Definition of acceptance evidence

A scenario can be considered verified through an appropriate combination of:

- automated unit tests;
- integration tests;
- API tests;
- frontend component/e2e tests;
- documented manual evidence for platform-specific build checks.

Manual evidence should not replace automation where behavior is deterministic and automatable.
