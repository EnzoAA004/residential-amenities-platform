# ADR-001 — Use a Private Monorepo

- **Status:** Accepted
- **Date:** 2026-09-27

## Context

Backend, client, infrastructure and documentation are parts of one product and are currently maintained by one developer.

## Decision

Use one private GitHub repository. `main` is the stable integration branch; short-lived branches merge through Pull Requests.

## Consequences

- Cross-cutting changes can be reviewed together.
- Documentation versions with implementation.
- CI can selectively build affected areas later.
- Repository boundaries may be reconsidered if independent teams/products appear.
