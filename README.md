# Residential Amenities Platform

Private-source, cloud-hosted platform for residential amenity reservations, payments and administration.

> **Project status:** Discovery / architecture bootstrap (v0.1)  
> **Pilot scope:** one residential building with 10 units (1A–5B).  
> **Product direction:** architecture prepared to evolve into a multi-building SaaS without expanding the MVP beyond what the pilot needs.

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

## Branching

`main` is the stable integration branch. Work is performed in short-lived branches such as `feat/*`, `fix/*`, `docs/*`, `infra/*`, `refactor/*` and `test/*`. Changes should reach `main` through Pull Requests.

## Source-code policy

This repository is private. End users and building administrators consume the hosted service; they do not receive repository, database or cloud-console access. Any future source-code transfer or licensing arrangement is a separate commercial/legal decision.
