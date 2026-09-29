# MVP Client Backlog — Phase 5

Status: **completed**. Master tracking issue **#56** is closed; child issues
**#44-#55** and backend prerequisites **#62** and **#66** are closed as
completed. The final frontend hardening PR was merged as #73.

This backlog turns [Phase 5 — MVP client / product UI](roadmap.md#phase-5--mvp-client--product-ui)
into incremental, independently mergeable work against the **existing,
stable backend contracts** (issues #19–#27, all merged to `main`). No backend
change is expected to support this phase; if one turns out to be genuinely
necessary, it is scoped as its own small issue rather than folded into a
frontend one.

Master tracking issue: **#56** (closed).

## Why this phase exists

The roadmap previously went straight from Phase 4 (Administration) to what is
now Phase 6 (QR entry point, push notifications, messaging). This backlog
inserted the missing step: build the resident- and administrator-facing
product UI against the backend that already exists.

## Issues

| # | GitHub issue | Issue | Depends on | Status |
| --- | --- | --- | --- | --- |
| 1 | #44 | Application shell, design system foundation and API client | — | Completed |
| 2 | #45 | Authentication, session persistence and route guards | #44 | Completed |
| 3 | #46 | Resident context, amenities and availability browsing | #45 | Completed |
| 4 | #47 | Leisure reservation flow (shared/exclusive) | #46 | Completed |
| 5 | #48 | Event reservation flow (slots + add-ons) | #46, #62 | Completed |
| 6 | #49 | Payment flows (Mercado Pago + cash) | #47, #48 | Completed |
| 7 | #50 | Resident reservation history and detail | #49, #66 | Completed |
| 8 | #51 | Administrator shell and dashboards (read models) | #45 | Completed |
| 9 | #52 | Administrator reservation operations (cancel/reschedule) | #51 | Completed |
| 10 | #53 | Administrator configuration (pricing/availability/event slots) | #51 | Completed |
| 11 | #54 | Audit trail and payment manual-review views | #51 | Completed |
| 12 | #55 | Frontend testing, accessibility and error handling | #44-#54 (incremental) | Completed |

Issues #62 and #66 were added and completed as narrow backend prerequisites
discovered during Phase 5: resident-facing Event slot discovery and
resident-owned reservation/payment reads.

## What stays out of this phase

- Refunds, accounting, invoice/receipt generation.
- QR entry point, push notifications, reservation-scoped messaging (Phase 6).
- Full-day reservations as a shipped, user-facing option (issue #2, OQ-003).
- Any hardcoded price, event time window, hold duration or cash-confirmer role
  — the client reads these from the backend (quotes, `EventSlotDefinition`,
  `ExpiresAtUtc`, the `Administrator` policy) and never assumes a final value.
- New backend endpoints beyond what already exists, unless a genuine gap is
  found — that becomes its own small, separately reviewed backend issue.
- Azure/Terraform/production deployment (Phase 7).
- NgRx/Redux or another global state library, unless a specific issue
  demonstrates local component state and Angular's built-in reactivity are
  insufficient — services with signals/RxJS are the default.

## Still blocked or deferred by issue #2

Nothing in this phase resolves issue #2. Concretely:

- **Prices and event hours**: the client always reads them from
  `GET /api/pricing/quote` and the resident-facing
  `GET /api/buildings/{id}/event-slots?date=` (issue #62) / amenity
  availability endpoints — never a constant in the client, and never the
  Administrator-only `GET /api/admin/buildings/{id}/event-slots`.
- **Hold duration**: the client reads `expiresAtUtc` from the reservation
  response and renders a countdown from it; it never assumes 30 minutes or any
  other fixed duration.
- **Full-day**: not exposed as a reservable option.
- **Cancellation/refund policy**: the resident UI has no self-service
  cancel/refund action (none exists on the backend either); the admin
  cancel screen surfaces the existing `requiresFinancialReview` flag rather
  than inventing refund UI.
- **Cash confirmer**: the admin cash-confirmation action is gated by the same
  `Administrator` policy the backend already enforces; no new role is added in
  the client.

## Definition of Done (applies to every issue below)

- Meets its own acceptance criteria.
- Has component/unit tests and, where the flow crosses a real HTTP call,
  tests against a mocked backend (no test depends on a live API).
- No secret, token or credential is embedded in client code or committed
  configuration; the backend remains the sole authority for authorization,
  pricing, availability and payment state (RNF-002, RNF-003, RNF-004).
- Keyboard-operable and screen-reader-reasonable for its own screens (full
  audit is issue 12, but a new screen should not regress obvious basics).
- Documented in the relevant client README section when it changes how the
  client is run, configured or structured.
