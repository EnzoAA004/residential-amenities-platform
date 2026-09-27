# ADR-006 — Use Microsoft Azure as Primary Cloud

- **Status:** Accepted
- **Date:** 2026-09-27

## Context

The project needs cloud hosting, but predictable spending and avoiding unnecessary billing surprises are important. The backend direction is also .NET.

## Options considered

- AWS
- Google Cloud
- Microsoft Azure

## Decision

Use Microsoft Azure as the primary cloud direction. Exact managed services must be selected after a cost/technical spike.

## Consequences

- Azure operational knowledge becomes part of the project.
- The stack aligns with the .NET learning path.
- Terraform will reduce manual portal dependence and keep infrastructure explicit.
