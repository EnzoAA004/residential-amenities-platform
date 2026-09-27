# ADR-007 — Use Terraform for Infrastructure as Code

- **Status:** Accepted
- **Date:** 2026-09-27

## Context

Cloud infrastructure should be reproducible, reviewable and suitable for professional DevOps practice.

## Decision

Use Terraform to provision/configure supported Azure infrastructure.

## Consequences

- Infrastructure changes can be reviewed through Git.
- Environments can be recreated consistently.
- Terraform state and credentials require secure handling.
- Portal-only changes should be minimized and reconciled when unavoidable.
