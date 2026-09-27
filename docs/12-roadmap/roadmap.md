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

## Phase 5 — UX extensions

- QR entry point.
- Push notifications.
- Reservation-scoped messaging.
- Accessibility/usability refinement.

## Phase 6 — Cloud/DevOps

- Docker production images.
- GitHub Actions.
- Azure staging.
- Terraform.
- Monitoring/secrets/backups.
- Production deployment.
- Cost budgets/alerts.

## Phase 7 — Productization

- Analytics.
- Tenant/building onboarding.
- Multi-building administration.
- Commercial/licensing/support model.

Puppet is introduced only if a real host-configuration use case exists; otherwise it remains a separate infrastructure-learning lab rather than production baggage.
