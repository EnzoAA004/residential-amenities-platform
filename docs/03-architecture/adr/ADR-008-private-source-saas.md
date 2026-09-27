# ADR-008 — Operate as a Private-Source Hosted Service

- **Status:** Accepted
- **Date:** 2026-09-27

## Context

The maintainer wants to retain control of source code, deployment and future scaling. Building users need the service, not the implementation repository.

## Decision

Keep source code, cloud administration, deployment configuration and database administration private. Customers/users consume the hosted web/mobile/API service.

Any future source-code delivery, escrow, licensing or ownership transfer is a separate commercial/legal agreement.

## Consequences

- Sensitive logic remains server-side wherever possible.
- Frontend artifacts cannot be treated as secret because browsers/devices must receive executable client code.
- Operations/support responsibility stays with the service operator unless contractually changed.
