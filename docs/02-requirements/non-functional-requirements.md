# Non-Functional Requirements

| ID | Requirement |
| --- | --- |
| RNF-001 | All production client-server traffic shall use HTTPS. |
| RNF-002 | Authorization shall be enforced server-side for every privileged action. |
| RNF-003 | Secrets, database credentials and payment-provider private credentials shall never be shipped in the client. |
| RNF-004 | The backend shall be the source of truth for pricing, availability and payment state. |
| RNF-005 | Reservation concurrency shall be handled so two incompatible bookings cannot both be confirmed for the same resources/time. |
| RNF-006 | Payment/webhook processing shall be idempotent. |
| RNF-007 | Important state changes shall be auditable. |
| RNF-008 | The application shall remain understandable for users with varied digital familiarity. |
| RNF-009 | The web client shall support current mainstream desktop and mobile browsers. |
| RNF-010 | Infrastructure configuration shall be reproducible through IaC where practical. |
| RNF-011 | Deployments shall be automated through CI/CD once cloud environments are introduced. |
| RNF-012 | Production secrets shall be managed outside source control. |
| RNF-013 | Logs shall avoid storing payment secrets, credentials or unnecessary personal information. |
| RNF-014 | Backups and restore procedures shall be defined before production use. |
| RNF-015 | Cloud costs shall be observable and guarded by budgets/alerts appropriate to the chosen Azure subscription. |
| RNF-016 | The system shall support future tenant/building isolation without global hard-coded building identifiers. |
| RNF-017 | The source repository shall remain private unless a separate licensing/commercial decision changes that policy. |
