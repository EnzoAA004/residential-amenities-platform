# MVP Client Backlog — Phase 5

This backlog turns [Phase 5 — MVP client / product UI](roadmap.md#phase-5--mvp-client--product-ui)
into incremental, independently mergeable work against the **existing,
stable backend contracts** (issues #19–#27, all merged to `main`). No backend
change is expected to support this phase; if one turns out to be genuinely
necessary, it is scoped as its own small issue rather than folded into a
frontend one.

Master tracking issue: **#56**.

## Why this phase exists

The roadmap previously went straight from Phase 4 (Administration) to what is
now Phase 6 (QR entry point, push notifications, messaging). The Angular +
Ionic + Capacitor client (issue #7) is still the Phase 1 technical scaffold —
shell, routing stub, one health-check screen — so none of those extensions
have a product to extend yet. This backlog inserts the missing step: build the
resident- and administrator-facing product UI against the backend that
already exists.

## Issues

| # | Issue | Depends on |
| --- | --- | --- |
| 1 | Application shell, design system foundation and API client | — |
| 2 | Authentication, session persistence and route guards | 1 |
| 3 | Resident context, amenities and availability browsing | 2 |
| 4 | Leisure reservation flow (shared/exclusive) | 3 |
| 5 | Event reservation flow (slots + add-ons) | 3 |
| 6 | Payment flows (Mercado Pago + cash) | 4, 5 |
| 7 | Resident reservation history and detail | 6 |
| 8 | Administrator shell and dashboards (read models) | 2 |
| 9 | Administrator reservation operations (cancel/reschedule) | 8 |
| 10 | Administrator configuration (pricing/availability/event slots) | 8 |
| 11 | Audit trail and payment manual-review views | 8 |
| 12 | Frontend testing, accessibility and error handling | 1–11 (incremental) |

Issues 4 and 5 can proceed in parallel once 3 is merged; issues 8–11 can
proceed in parallel with 4–7 once 2 is merged (they only need the
Administrator route guard, not the resident booking flow). Issue 12 lands
incrementally alongside each of the others (each issue's own acceptance
criteria include its tests and accessibility pass); the final pass of issue 12
is what confirms the whole phase together.

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
