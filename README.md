# Residential Amenities Platform

Private-source, cloud-hosted platform for residential amenity reservations, payments and administration.

> **Project status:** Backend MVP and MVP product client are implemented and covered by CI (issues #3-#27, #44-#55, #62 and #66); cloud deployment and several building-specific business decisions (issue #2, still open) remain pending.
> **Pilot scope:** one residential building with 10 units (1A–5B).
> **Product direction:** architecture prepared to evolve into a multi-building SaaS without expanding the MVP beyond what the pilot needs.

### What exists today

- **Backend (`apps/api`):** identity/RBAC (Resident, Administrator), building/unit/membership, amenities and availability, shared/exclusive leisure and event reservations, server-authoritative pricing with historical snapshots, concurrency control (advisory locks, holds, expiration), Mercado Pago payments, cash payments, an append-only audit trail, and administrative operations (reservation/payment queries, cancel, reschedule, pricing, availability, event slots). Backend and full-stack validation are green in CI.
- **Client (`apps/client`):** Angular + Ionic product client through Phase 5: authentication/session guards, resident amenity browsing, leisure/event reservation creation, Mercado Pago and cash payment flows, resident reservation history/detail, administrator dashboards/operations/configuration/audit/manual-review views, shared error handling and frontend hardening. See [the completed MVP client backlog](docs/12-roadmap/backlog-mvp-client.md).
- **Cloud/DevOps:** local Docker Compose + GitHub Actions CI only. Azure, Terraform and production deployment are not started.
- **Open business decisions (issue #2):** definitive prices, event time windows, full-day bookings, hold duration, cancellation/refund policy, cleaning rules, and who is authorized to confirm cash receipt remain open. Where the backend needs a value today it uses an explicit, documented, configurable placeholder — never a decision presented as final.

## Goals

- Replace informal/manual booking coordination with a clear availability calendar.
- Support shared leisure bookings, exclusive leisure bookings and longer event slots.
- Integrate online payments through Mercado Pago and controlled cash-payment confirmation.
- Provide an administrator panel with traceability, history and configuration.
- Keep source code and cloud operations under the product owner's control.
- Build the project with professional engineering practices: documentation-as-code, CI/CD, IaC, security, testing and FinOps.

## Technology baseline

| Area | Technology |
| --- | --- |
| Backend | C# / ASP.NET Core (.NET) |
| Client | Angular + Ionic + Capacitor |
| Database | PostgreSQL |
| Realtime | SignalR |
| Cloud | Microsoft Azure |
| Containers | Docker |
| IaC | Terraform |
| CI/CD | GitHub Actions |
| Configuration management | Puppet, only where host-level configuration is justified |
| Payments | Mercado Pago |
| Repository | Private GitHub monorepo |

## Repository layout

```text
apps/
  api/
  client/
infrastructure/
  terraform/
  puppet/
docs/
  00-project/
  01-discovery/
  02-requirements/
  03-architecture/
  04-data/
  06-security/
  07-devops/
  09-finops/
  12-roadmap/
.github/
scripts/
```

## Documentation

- [Vision](docs/00-project/vision.md)
- [Scope](docs/00-project/scope.md)
- [Decision log](docs/01-discovery/decision-log.md)
- [Functional requirements](docs/02-requirements/functional-requirements.md)
- [Business rules](docs/02-requirements/business-rules.md)
- [Architecture overview](docs/03-architecture/architecture-overview.md)
- [Domain model](docs/04-data/domain-model.md)
- [Security baseline](docs/06-security/security-baseline.md)
- [DevOps strategy](docs/07-devops/devops-strategy.md)
- [Continuous Integration](docs/07-devops/ci.md)
- [CAPEX / OPEX](docs/09-finops/capex-opex.md)
- [Roadmap](docs/12-roadmap/roadmap.md)
- [MVP client backlog](docs/12-roadmap/backlog-mvp-client.md)

## Branching

`main` is the stable integration branch. Work is performed in short-lived branches such as `feat/*`, `fix/*`, `docs/*`, `infra/*`, `refactor/*` and `test/*`. Changes should reach `main` through Pull Requests.

## Source-code policy

This repository is private. End users and building administrators consume the hosted service; they do not receive repository, database or cloud-console access. Any future source-code transfer or licensing arrangement is a separate commercial/legal decision.
