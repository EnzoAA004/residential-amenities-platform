# ADR-009 — Keep the Domain Multi-Building-Ready

- **Status:** Accepted
- **Date:** 2026-09-27

## Context

The first deployment is one building, but the product may later be offered to other buildings/consortia.

## Decision

Model building/tenant ownership explicitly and avoid global assumptions tied to the pilot building. Do not implement full SaaS control-plane complexity in MVP.

## Consequences

- Core entities need an ownership path to a building/tenant.
- Authorization queries must respect that boundary.
- Future tenant isolation can evolve without replacing the reservation domain.
- MVP may remain operationally single-tenant if simpler.
