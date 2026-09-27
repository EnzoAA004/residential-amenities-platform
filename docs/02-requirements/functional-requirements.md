# Functional Requirements

Status legend: **MVP**, **Post-MVP**, **TBD**.

| ID | Requirement | Priority |
| --- | --- | --- |
| RF-001 | The system shall authenticate residents and administrators. | MVP |
| RF-002 | The system shall associate residents with an authorized building/unit membership. | MVP |
| RF-003 | The system shall display amenity availability by date and time. | MVP |
| RF-004 | A resident shall be able to create a short shared-leisure SUM reservation. | MVP |
| RF-005 | A resident shall be able to create a short exclusive-leisure SUM reservation. | MVP |
| RF-006 | A resident shall be able to create an event reservation using configured event time windows. | MVP |
| RF-007 | A qualifying event reservation shall allow optional resources such as pool and barbecue/grill. | MVP |
| RF-008 | The system shall calculate the quoted price from server-side pricing rules. | MVP |
| RF-009 | The system shall preserve the price quoted/accepted for an existing reservation even if future pricing changes. | MVP |
| RF-010 | The system shall prevent incompatible overlapping reservations. | MVP |
| RF-011 | The system shall create a configurable temporary hold while payment is pending. | MVP |
| RF-012 | The system shall release expired unpaid holds automatically. | MVP |
| RF-013 | A resident shall be able to initiate payment through Mercado Pago. | MVP |
| RF-014 | The backend shall update payment/reservation state from trusted payment-provider confirmation. | MVP |
| RF-015 | A resident shall be able to declare cash as the intended payment method. | MVP |
| RF-016 | An authorized administrator shall be able to confirm cash receipt. | MVP |
| RF-017 | An administrator shall be able to view reservation and payment details. | MVP |
| RF-018 | An administrator shall be able to cancel/reschedule a reservation subject to business rules and audit logging. | MVP |
| RF-019 | An administrator shall be able to configure prices and applicable effective periods. | MVP |
| RF-020 | An administrator shall be able to configure reservable time windows/rules. | MVP |
| RF-021 | The system shall retain an auditable history for important reservation/payment/admin transitions. | MVP |
| RF-022 | The client shall expose a responsive web experience suitable for desktop and mobile screens. | MVP |
| RF-023 | The client shall be packageable for Android/iOS using the selected cross-platform stack. | MVP |
| RF-024 | A QR entry point shall open the booking experience without exposing secrets. | Post-MVP |
| RF-025 | The system shall deliver user notifications for meaningful reservation/payment events. | Post-MVP |
| RF-026 | Users sharing a compatible reservation window shall have reservation-scoped messaging. | Post-MVP |
| RF-027 | The administrator shall have basic usage/payment analytics. | Post-MVP |
| RF-028 | The product shall support onboarding additional buildings/tenants without redesigning core reservation ownership. | Post-MVP |
| RF-029 | A resident shall be able to request a full-day booking composed of compatible event windows. | TBD |
