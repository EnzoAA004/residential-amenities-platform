# CAPEX / OPEX View

This document is a project/FinOps planning view, not formal accounting advice.

## OPEX-oriented model

The hosted-service model is primarily operating expenditure because the platform is expected to incur recurring costs for cloud operation and supporting services.

| Cost area | Typical project treatment |
| --- | --- |
| Azure application runtime | OPEX |
| Managed PostgreSQL | OPEX |
| Storage/backups | OPEX |
| Monitoring/logging | OPEX |
| Domain/DNS | OPEX |
| Transaction/payment fees | Variable OPEX |
| Notification provider usage | OPEX / usage-based |
| Maintenance/support | OPEX |
| Development tools/subscriptions | OPEX unless a specific accounting policy says otherwise |

## Up-front investment view

Initial analysis, design and implementation are a one-time project investment from a planning perspective. Whether any development expenditure is capitalized for accounting purposes depends on the legal/entity/accounting context and is intentionally not decided here.

## FinOps principles

1. Start with the smallest viable managed footprint.
2. Avoid always-on resources unless justified.
3. Use budgets and alerts before production.
4. Keep staging smaller/ephemeral where possible.
5. Review logs/monitoring retention.
6. Measure cost per building, resident and reservation as the product grows.
7. Treat payment-provider fees separately from cloud infrastructure cost.
