# ADR-012 - Mobile biometric sign-in via WebAuthn/passkeys

- **Status:** Accepted
- **Date:** 2026-09-30

## Context

Issue #94 (DEC-014): residents should be able to sign in with fingerprint/
Face ID on mobile. The hard requirement is that biometric data never
reaches the backend — the server may only ever see standard cryptographic
credentials/tokens, gated behind device-level biometric unlock. The issue
explicitly lists three acceptable approaches and asks that one be picked
and documented before implementation:

1. A platform biometric-protected credential/session unlock (OS gates
   access to a securely-stored token; the token is what reaches the
   server).
2. Passkeys/WebAuthn.
3. Capacitor/native secure credential storage unlocked by the device's
   biometric API.

As already noted in ADR-011 (push notifications), this client has
Capacitor initialized at the JS/config level (`capacitor.config.ts`,
`@capacitor/core|android|ios` dependencies) but **no native platform
project has been generated** (`npx cap add android|ios` was never run —
there is no `android/` or `ios/` directory). No biometric Capacitor plugin
is installed. Building and testing option 3 would require Android Studio/
Xcode, a real device or emulator with enrolled biometrics, and a native
build step none of which this environment provides — and it cannot be
validated here without that tooling.

## Decision

Use **WebAuthn/passkeys** (option 2).

- WebAuthn is a W3C/FIDO standard implemented directly by the OS and
  browser engine (including inside a Capacitor WebView on a real device)
  — it requires no native plugin, no native build, and no platform SDK to
  implement or to exercise the real cryptographic verification path in
  tests.
- The device/OS is the only party that ever sees the biometric sensor; the
  browser mediates a public-key credential ceremony and the server
  (`Fido2NetLib`, a mature, widely-used .NET FIDO2/WebAuthn library) only
  ever receives a public key, a signature and a credential id — never a
  fingerprint template, face data, or anything derived from one.
- This naturally satisfies "falling back to email/password continues to
  work unchanged": WebAuthn is registered as a second, independent sign-in
  path alongside the existing `/api/auth/login`, which is not modified.
- If/when this client's Capacitor native projects are actually generated
  (a separate, larger undertaking outside this issue's scope), the same
  WebAuthn ceremony still works unchanged inside the native WebView; a
  native secure-storage credential unlock (option 1/3) could be added
  later as an *additional* path without requiring this one to change.

## Consequences

- Registration (`POST /api/auth/webauthn/register/options` then
  `.../register`) requires an already-authenticated session — biometric
  sign-in unlocks an *existing* account, it never creates one, matching
  the issue's own framing and #93's admin-created-account model.
- Login (`POST /api/auth/webauthn/login/options` then `.../login`) is
  anonymous by necessity (the resident is not signed in yet) but never
  confirms whether an email has a registered credential any more
  specifically than "biometric sign-in is not available for this
  account" — the client always falls back to the password form in that
  case.
- No production passkey/attestation policy (e.g. requiring specific
  authenticator attestation, enterprise MDS metadata) is decided; this
  accepts `None` attestation and does not verify device attestation
  chains, consistent with a typical consumer passkey deployment and with
  not inventing policy beyond what #94 asked for.
- Real end-to-end coverage (an actual phone's fingerprint sensor driving a
  real browser through the full ceremony) is not something this
  environment can execute. The backend ceremony is tested directly against
  `Fido2NetLib`'s real verification logic using a software credential
  (a real ES256 keypair and spec-shaped authenticator data/attestation
  object, not a mock that skips verification) — this exercises the actual
  cryptographic checks the library performs, short of a physical sensor.
