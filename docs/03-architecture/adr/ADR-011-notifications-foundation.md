# ADR-011 - Push notifications foundation

- **Status:** Accepted
- **Date:** 2026-09-30

## Context

Phase 6 needs the technical foundation for reservation/payment notifications
without deciding final business rules or production cloud infrastructure.
The client is Angular/Ionic with Capacitor initialized, but native platform
directories are not committed yet. Browser push also has hard platform
requirements: HTTPS secure context, notification permission, a Service Worker
and an application-server key such as VAPID.

## Decision

Use an explicit opt-in registration model:

- The client only calls the backend after notification permission is granted.
- The backend stores notification subscriptions under the authenticated user;
  clients never submit a user id.
- The first implemented platform contract is `WebPush` with endpoint, `p256dh`
  and `auth` values. `CapacitorNative` is reserved for future native APNs/FCM
  token registration after Android/iOS projects exist.
- Backend read responses return safe metadata only: subscription id, platform,
  endpoint hash, enabled flag and timestamps. They do not echo raw endpoints or
  cryptographic secrets.
- No notification send pipeline is created in this phase.

## Consequences

- Future flows can ask the user from a clear UI action and then register or
  unregister the resulting subscription.
- Tokens are not persisted when permission is denied or notifications are not
  available.
- The API is ready for a future sender, but production delivery still requires
  selecting/configuring the push provider, Service Worker registration,
  VAPID/public key delivery for web, and native APNs/FCM setup for Capacitor.
- Reservation/payment notification timing, templates and audience rules remain
  out of scope.
