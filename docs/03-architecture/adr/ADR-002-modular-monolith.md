# ADR-002 — Start as a Modular Monolith

- **Status:** Accepted
- **Date:** 2026-09-27

## Context

The pilot serves one small building. Distributed systems would increase operational complexity without a demonstrated scaling need.

## Decision

Build one backend deployment with explicit internal modules.

## Consequences

- Lower deployment and debugging overhead.
- Transactions and consistency are easier to manage.
- Module boundaries must remain intentional.
- Services can be extracted later only when evidence justifies it.
