# ADR-004 — Use PostgreSQL

- **Status:** Accepted
- **Date:** 2026-09-27

## Context

Reservations, pricing, memberships, payments and audit history are relational and require strong consistency/concurrency handling.

## Decision

Use PostgreSQL as the primary database in development and production.

## Consequences

- Consistent relational model and transactional behavior.
- Suitable for advanced time/range/index strategies if needed.
- Local development should use PostgreSQL (for example via Docker) rather than relying on SQLite behavior.
