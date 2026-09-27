# Scope

## MVP in scope

- Resident authentication and authorization.
- Building/unit membership.
- Availability calendar.
- SUM leisure reservations.
- Shared and exclusive leisure modes.
- Longer event reservations by configured time windows.
- Optional event resources: barbecue/grill and swimming pool.
- Configurable prices and booking rules.
- Temporary reservation hold while payment is pending.
- Mercado Pago payment flow.
- Cash-payment declaration and administrator confirmation.
- Reservation lifecycle/history.
- Administrator dashboard for reservations, payments and configuration.
- Auditability of administrative actions.
- Responsive web experience and installable/mobile client through Ionic/Capacitor.

## Planned after the booking/payment core

- QR entry point to booking flow.
- Push notifications.
- Reservation-scoped messaging for shared usage.
- Usage/revenue analytics.
- Stronger multi-building administration.

## Explicitly out of MVP

- General-purpose building social network.
- Full WhatsApp-like chat.
- Microservices.
- Kubernetes.
- Public self-registration without building approval/invitation.
- Direct database access for building administrators.
- Source-code delivery to customers.
- Complex ERP/accounting functions.
- Automatic physical-access control.

## Constraints

- Initial population: one building and 10 residential units.
- Users include both younger and older residents; usability has priority over novelty.
- Cloud spending must remain predictable and intentionally enabled.
- Prices, time windows and some operational policies are provisional and must be validated with stakeholders.

## Future product boundary

The system may evolve into a multi-building SaaS. That future direction influences identifiers, ownership boundaries and tenant-aware data design, but must not force premature infrastructure complexity into the pilot.
