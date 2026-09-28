# MVP Traceability Matrix

This matrix connects MVP requirements to implementation work and expected verification.

## Functional requirements

| Requirement | Implementation issue(s) | Primary verification |
| --- | --- | --- |
| RF-001 Authenticate residents/admins | #10 | Auth integration tests; protected endpoint tests |
| RF-002 Authorized building/unit membership | #9, #10 | Membership persistence + authorization tests |
| RF-003 Display amenity availability | #19 | Availability API integration tests |
| RF-004 Shared-leisure reservation | #20 | Reservation compatibility tests |
| RF-005 Exclusive-leisure reservation | #20 | Exclusive blocking tests |
| RF-006 Event reservation | #21 | Event-slot reservation tests |
| RF-007 Optional pool/barbecue resources | #21 | Resource-combination tests |
| RF-008 Server-side price calculation | #22 | Pricing unit/integration tests |
| RF-009 Preserve historical quoted price | #22 | Price snapshot regression test |
| RF-010 Prevent incompatible overlaps | #23 | Concurrency/integration tests |
| RF-011 Configurable payment hold | #23 | Hold creation tests |
| RF-012 Release expired unpaid holds | #23 | Expiration/idempotency tests |
| RF-013 Initiate Mercado Pago payment | #24 | `PaymentFlowTests`, `MercadoPagoHttpClientTests`, `PaymentDomainTests` (fake provider, no network) |
| RF-014 Update state from trusted provider confirmation | #24 | `MercadoPagoSignatureVerifierTests`, `PaymentFlowTests` (signed webhook + server-side re-fetch), `PaymentReservationRaceTests` |
| RF-015 Declare cash payment method | #25 | `CashPaymentFlowTests` (declaration, snapshot amount, idempotent/concurrent declaration, method exclusivity), `PaymentDomainTests` |
| RF-016 Authorized cash confirmation | #25 | `CashPaymentFlowTests` (Administrator-only 403/401, actor from session, idempotent confirmation, cash-vs-expiration race, late cash) |
| RF-017 Admin views reservation/payment detail | #26 | Admin query/API tests |
| RF-018 Admin cancel/reschedule | #26 | Admin command + audit tests |
| RF-019 Configure prices/effective periods | #22, #26 | Pricing configuration tests |
| RF-020 Configure reservable windows/rules | #19, #26 | Availability configuration tests |
| RF-021 Auditable transition history | #27 | `AuditTrailTests` (events for reservations, cash, Mercado Pago, expiration, identity; no duplicates; atomic rollback; admin query), `AuditDomainTests` |
| RF-022 Responsive web experience | #7 | Client component/e2e viewport checks |
| RF-023 Android/iOS packageable client | #7 | Capacitor build/config validation |

## Non-functional requirements

| Requirement | Primary issue(s) / verification |
| --- | --- |
| RNF-001 HTTPS in production | #13, future cloud deployment issue |
| RNF-002 Server-side authorization | #10, #26 |
| RNF-003 No private credentials in client | #13, #24 |
| RNF-004 Backend is authority for price/availability/payment | #19, #22, #24 |
| RNF-005 Reservation concurrency correctness | #23 |
| RNF-006 Idempotent payment/webhook processing | #24 (persisted idempotency key, unique provider event/order ids, state-based reconciliation — `PaymentFlowTests`, `PaymentReservationRaceTests`), #25 (cash: one active payment per reservation, row-locked idempotent confirmation — `CashPaymentFlowTests`) |
| RNF-007 Auditable important transitions | #27 (append-only `AuditLogs`, atomic with the transition — `AuditTrailTests`) |
| RNF-008 Understandable for varied digital familiarity | #7, future usability validation |
| RNF-009 Mainstream browser support | #7 |
| RNF-010 Reproducible IaC | future Phase 6 Terraform issue |
| RNF-011 Automated deployments | #12 plus future Phase 6 deployment issue |
| RNF-012 Secrets outside source control | #13 |
| RNF-013 Safe logging | #13, #27 (allowlisted audit metadata; no secrets/tokens/e-mails audited — `AuditDomainTests`, `AuditTrailTests`) |
| RNF-014 Backup/restore before production | future Phase 6 operations issue |
| RNF-015 Cost observability/budgets | future Phase 6 FinOps issue |
| RNF-016 Future tenant/building isolation | #9 and ADR-009 |
| RNF-017 Private source repository | repository policy / ADR-008 |

## Traceability rule

Every implementation PR should reference:

1. the GitHub issue it closes or advances;
2. the relevant RF/RNF/RB identifiers;
3. tests or evidence that verify the acceptance criteria.

When a requirement changes, update this matrix in the same PR as the requirement/decision change.
