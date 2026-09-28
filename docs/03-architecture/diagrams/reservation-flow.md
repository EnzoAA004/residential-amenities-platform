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

Implemented as of issue #25. `Pending` is the payment hold (the conceptual
`PendingPayment` above); `Draft` and `PendingCashConfirmation` do not exist
yet.

```mermaid
stateDiagram-v2
    [*] --> Pending: reservation created (hold, RB-009)
    Pending --> Confirmed: trusted payment confirmation (#24)
    Pending --> Expired: ExpiresAtUtc passed (#23)
    Pending --> Cancelled
    Confirmed --> Cancelled
    Expired --> [*]
    Cancelled --> [*]
```

- `Pending → Confirmed` happens **only** through Reservations'
  `ConfirmPaidReservationAsync`, called by Payments after it has verified a
  Mercado Pago order server-side (RB-011). A browser return URL never does it.
- `Pending → Expired` is the **competing** transition. Both are serialized
  on the reservation row (see
  [Payments and Mercado Pago](../../04-data/domain-model.md#payments-and-mercado-pago-issue-24)):
  exactly one wins, and a reservation can never be both.
- **`Expired` is terminal.** A payment the provider approves *after* the hold
  expired does not revive the reservation (its resources were released and
  may already belong to someone else). The payment is recorded as approved
  with an explicit `ApprovedAfterExpiry` outcome requiring manual review.
- `PendingCashConfirmation` arrives with #25; `Draft`/`Completed` are not
  implemented.
