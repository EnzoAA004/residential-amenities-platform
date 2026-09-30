# Roadmap

## Phase 0 — Discovery and project foundation

- Validate resident/admin workflows.
- Close prices/time-window/cancellation/cleaning questions.
- Establish documentation and ADR process.
- Create backlog and acceptance criteria.
- Confirm technology spikes.

## Phase 1 — Local technical foundation

- ASP.NET Core solution.
- Angular/Ionic client.
- PostgreSQL via Docker.
- Authentication foundation.
- Building/unit/resident membership model.
- Initial automated tests.

## Phase 2 — Reservation core

- Availability.
- Resource/time model.
- Shared vs exclusive leisure.
- Event slots.
- Pricing rules and snapshots.
- Concurrency protection.
- Hold/expiration lifecycle.

## Phase 3 — Payments

- Mercado Pago integration.
- Verified/idempotent provider event handling.
- Cash declaration/confirmation.
- Payment/reservation audit history.

## Phase 4 — Administration

- Reservation/payment dashboard.
- Reschedule/cancel flows.
- Price/time configuration.
- Audit views.

## Phase 5 — MVP client / product UI

Status: **completed**. The backend MVP (Phases 1-4) and the Angular + Ionic +
Capacitor MVP product client are implemented, tested in CI and merged. The
client now has the product's own resident and administrator screens against
the stable backend contracts.

- Application shell, design system foundation and an authenticated HTTP/API
  client layer.
- Authentication, session persistence and protected routing (Resident vs
  Administrator).
- Building/membership context and amenity availability browsing.
- Leisure reservation flow (shared and exclusive).
- Event reservation flow (configured slots, pool/barbecue add-ons).
- Payment flows: Mercado Pago checkout and cash declaration.
- Resident reservation history and detail (status, hold countdown, payment
  outcome).
- Administrator shell and reservation/payment dashboards (read models).
- Administrator reservation operations (cancel, reschedule).
- Administrator configuration (pricing, availability, event slots).
- Audit trail and payment-manual-review views for administrators.
- Frontend testing, accessibility and error-handling hardening.

Business-policy issue #2 is now closed for MVP/pilot scope: prices, Event
hours, payment hold duration and cash-confirmation authority are decided and
surfaced from backend configuration/state, never hardcoded in the client.
Full-day reservations remain deferred, and the financial/refund consequence
of a late/disallowed Event cancellation remains an explicit follow-up rather
than invented behavior. See [the completed MVP client backlog](backlog-mvp-client.md)
for the issue breakdown and closure state.

## Phase 6 — UX extensions

Status: **completed**. #79 remains open/deferred until real staging or pilot
usage feedback exists; no arbitrary UX refinement was invented.

- QR entry point.
- Push notifications.
- Reservation-scoped messaging.
- Accessibility/usability refinement beyond the MVP client baseline.

## Phase 7 — Cloud/DevOps

- Docker production images.
- Container security.
- Azure staging.
- Terraform.
- Secrets management, observability, backups.
- Production deployment, gated by approval.
- Cost budgets/alerts.

Runs after (or, where independent — e.g. Terraform authoring, container
hardening — in parallel with) Phase 5; production deployment needs a real
client to deploy. Puppet is not introduced here artificially: it stays a
separate infrastructure lab unless a real host/VM configuration need appears
(see [DevOps strategy](../07-devops/devops-strategy.md)).

## Phase 8 — Productization

- Analytics.
- Tenant/building onboarding.
- Multi-building administration.
- Commercial/licensing/support model.

Puppet is introduced only if a real host-configuration use case exists; otherwise it remains a separate infrastructure-learning lab rather than production baggage.
