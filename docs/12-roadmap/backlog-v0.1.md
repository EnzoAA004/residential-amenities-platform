# Initial Backlog v0.1

This backlog translates the project roadmap into the first executable work items.

Master tracking issue: #14

## Phase 0 — Discovery / Foundation

| Issue | Work item | Goal |
| --- | --- | --- |
| #2 | Validate booking, payment and operational rules | Close open stakeholder questions before hardcoding business behavior. |
| #3 | Refine modular-monolith boundaries | Define module responsibilities and dependencies before backend scaffold. |
| #4 | Validate local toolchain | Prove .NET + Angular/Ionic + PostgreSQL + Docker work together locally. |
| #5 | Define MVP acceptance criteria and traceability | Connect requirements to implementation issues and tests. |

## Phase 1 — Local Technical Foundation

| Issue | Work item | Goal |
| --- | --- | --- |
| #6 | ASP.NET Core scaffold | Establish the backend solution and modular structure. |
| #7 | Angular/Ionic/Capacitor scaffold | Establish the responsive web/mobile client. |
| #8 | PostgreSQL + Docker Compose | Provide reproducible local persistence and migrations. |
| #9 | Building/Unit/User/Membership domain | Implement the first multi-building-ready domain slice. |
| #10 | Authentication and RBAC | Establish Resident/Admin access control. |
| #11 | Testing baseline | Add unit and integration testing foundations. |
| #12 | GitHub Actions CI | Automate build and tests for Pull Requests. |
| #13 | Security/config baseline | Define secret handling, safe configuration and logging. |

## Recommended execution order

```text
#2 Discovery ─────────────┐
#3 Architecture ─────────┼──> #5 Acceptance/traceability
#4 Toolchain spike ──────┘
             │
             ├──> #6 Backend ──> #8 PostgreSQL ──> #9 Domain ──> #10 Auth
             │
             └──> #7 Frontend

#11 Testing and #13 Security are introduced during the scaffold.
#12 CI becomes enforceable once build/test commands exist.
```

## Working agreement

Each implementation item should normally follow:

```text
Issue
  ↓
short-lived branch
  ↓
implementation + documentation + tests
  ↓
Pull Request
  ↓
CI
  ↓
main
```

## Current scope boundary

This backlog intentionally stops before Reservation Core implementation. Phase 2 will be decomposed after the local foundation is executable and the open building policies have enough validated answers.

Future backlog areas:

- Phase 2 — Reservation Core
- Phase 3 — Payments
- Phase 4 — Administration
- Phase 5 — UX Extensions
- Phase 6 — Cloud / DevOps
- Phase 7 — Productization
