# Reservation Flow

```mermaid
stateDiagram-v2
    [*] --> Draft
    Draft --> PendingPayment: confirm request
    PendingPayment --> Confirmed: online payment confirmed
    PendingPayment --> Expired: hold timeout
    PendingPayment --> PendingCashConfirmation: cash selected
    PendingCashConfirmation --> Confirmed: authorized cash confirmation
    PendingCashConfirmation --> Cancelled: cancellation / timeout policy
    Confirmed --> Cancelled: approved cancellation
    Confirmed --> Completed: booking window completed
    Expired --> [*]
    Cancelled --> [*]
    Completed --> [*]
```

Exact states and transitions will be refined during domain modeling. This records the current conceptual lifecycle.

## Current implementation status

Issue #20 implements only `[*] --> Confirmed` and `Confirmed --> Cancelled`
(the latter as a domain method, not yet exposed over HTTP) for Shared and
Exclusive Leisure. There is no payment step yet, so reservations skip
`Draft`/`PendingPayment` entirely and are `Confirmed` immediately on
creation. `PendingPayment`, `PendingCashConfirmation`, `Expired` and
`Completed` are introduced by #23 (holds/expiration), #24 (Mercado Pago) and
#25 (cash) as those flows are actually built — see
`docs/04-data/domain-model.md#reservations--sharedexclusive-leisure-issue-20`.
