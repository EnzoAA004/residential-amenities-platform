# Decision Log

This is the lightweight product/technical decision history. Architecture-level decisions also receive a dedicated ADR.

| ID | Date | Decision | Status |
| --- | --- | --- | --- |
| DEC-001 | 2026-09-27 | Use one private monorepo with `main` as stable integration branch. | Accepted |
| DEC-002 | 2026-09-27 | Backend technology: C# / ASP.NET Core instead of the maintainer's usual Java/Spring Boot stack. | Accepted |
| DEC-003 | 2026-09-27 | Client direction: Angular + Ionic + Capacitor for web/mobile. | Accepted |
| DEC-004 | 2026-09-27 | Primary relational database: PostgreSQL. | Accepted |
| DEC-005 | 2026-09-27 | Use Microsoft Azure instead of AWS as primary cloud direction. | Accepted |
| DEC-006 | 2026-09-27 | Use Terraform for Infrastructure as Code. | Accepted |
| DEC-007 | 2026-09-27 | Puppet is optional configuration management and should only be introduced where host-level configuration is justified. | Accepted |
| DEC-008 | 2026-09-27 | Source code remains private; customers receive access to the hosted service, not the repository. | Accepted |
| DEC-009 | 2026-09-27 | Start with a modular monolith, not microservices. | Accepted |
| DEC-010 | 2026-09-27 | Preserve a future multi-building boundary in the domain model without expanding MVP scope. | Accepted |
| DEC-011 | 2026-09-27 | Prices and time windows must be configurable rather than hard-coded. | Accepted |
| DEC-012 | 2026-09-27 | Documentation is versioned with the code and updated with relevant PRs. | Accepted |
| DEC-013 | 2026-09-30 | Pilot price configuration validated: SUM Event base ARS 50,000; Pool Event add-on ARS 10,000; Barbecue Event add-on ARS 10,000; Shared Leisure ARS 2,000; Exclusive Leisure ARS 5,000. Prices remain configurable/effective-dated and historical reservations retain their price snapshot (RB-007, RB-008). Does not resolve whether Pool/Barbecue can be booked independently of SUM (OQ-014, still open). | Accepted |

## Change policy

Do not rewrite past decisions to make them look inevitable. When a meaningful decision changes, record the new decision and reference the one it supersedes.
