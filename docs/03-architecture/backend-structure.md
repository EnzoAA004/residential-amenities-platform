# Backend Structure

Issue: #6

## Decision

The first production-oriented backend remains a **single ASP.NET Core project** inside the modular monolith.

Logical modules are represented by explicit folders/namespaces. They are not split into separate .NET projects yet.

This avoids creating project/reference overhead before any module contains enough implementation to justify it.

## Composition root

`apps/api/Program.cs` is responsible for:

- framework/infrastructure registration;
- module registration;
- middleware order;
- endpoint mapping.

It must not accumulate reservation/payment business rules.

## Initial module namespaces

```text
ResidentialAmenities.Api.Modules.Identity
ResidentialAmenities.Api.Modules.Buildings
ResidentialAmenities.Api.Modules.Amenities
ResidentialAmenities.Api.Modules.Reservations
ResidentialAmenities.Api.Modules.Pricing
ResidentialAmenities.Api.Modules.Payments
ResidentialAmenities.Api.Modules.Administration
ResidentialAmenities.Api.Modules.Audit
ResidentialAmenities.Api.Modules.Notifications
ResidentialAmenities.Api.Modules.Messaging
ResidentialAmenities.Api.Modules.Reporting
```

Messaging and Reporting are post-MVP boundaries. Their registration exists only to reserve an explicit place in the modular architecture; they contain no business behavior yet.

## Infrastructure

Cross-cutting technical concerns live outside business modules when they are not owned by one domain capability.

Current examples:

- centralized exception handling;
- PostgreSQL technical connection setup;
- OpenAPI generation;
- CORS/configuration.

Persistence will be formalized in issue #8.

## API style

The initial API uses Minimal APIs. Endpoints should be grouped/extracted from `Program.cs` rather than implemented as large inline handlers.

The choice does not prohibit controllers later where they improve organization, but introducing two styles must have a concrete reason.

## OpenAPI

ASP.NET Core first-party OpenAPI generation is enabled. The document endpoint is mapped only in Development.

## Evolution

A logical module may later become a separate .NET project if that improves dependency enforcement. Becoming a separate deployable service requires stronger evidence documented in the modular-monolith ADR.
