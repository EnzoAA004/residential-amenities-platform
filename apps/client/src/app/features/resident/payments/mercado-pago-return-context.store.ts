import { Injectable } from '@angular/core';

/** Namespaced so it never collides with anything else this origin might store. */
const STORAGE_KEY = 'residential-amenities:mercadopago-return';

/**
 * The only two ids needed to correlate the browser's return from Mercado
 * Pago's Checkout Pro back to the payment it started (issue #49). Neither
 * is a credential or auth state — they are the same reservation/payment
 * UUIDs the resident's own session already has access to.
 */
export interface MercadoPagoReturnContext {
  paymentId: string;
  reservationId: string;
}

function isValidContext(value: unknown): value is MercadoPagoReturnContext {
  if (typeof value !== 'object' || value === null) {
    return false;
  }

  const candidate = value as Record<string, unknown>;
  return typeof candidate['paymentId'] === 'string' && typeof candidate['reservationId'] === 'string';
}

/**
 * Correlates the return from Mercado Pago's Checkout Pro to the payment
 * that started it, using `sessionStorage` only.
 *
 * This is **not** an auth/session persistence mechanism: it never stores a
 * token, cookie, role, user object, membership, `checkoutUrl`, or any
 * payment amount/status. The backend's own `back_urls` configuration is
 * UX-only and can carry provider query parameters the browser cannot be
 * trusted to interpret (RB-011) — this store exists so the return page
 * never has to read them: it reads `paymentId`/`reservationId` from here
 * instead, and always asks the backend for the real payment state.
 */
@Injectable({
  providedIn: 'root'
})
export class MercadoPagoReturnContextStore {
  save(context: MercadoPagoReturnContext): void {
    try {
      sessionStorage.setItem(
        STORAGE_KEY,
        JSON.stringify({ paymentId: context.paymentId, reservationId: context.reservationId })
      );
    } catch {
      // sessionStorage can throw (private browsing, disabled storage, quota).
      // The return page's "no context" state already handles a missing
      // entry safely, so there is nothing further to do here.
    }
  }

  /** Returns `null` for a missing, malformed, or partial entry — and clears it in that case. */
  read(): MercadoPagoReturnContext | null {
    let raw: string | null;

    try {
      raw = sessionStorage.getItem(STORAGE_KEY);
    } catch {
      return null;
    }

    if (!raw) {
      return null;
    }

    let parsed: unknown;

    try {
      parsed = JSON.parse(raw);
    } catch {
      this.clear();
      return null;
    }

    if (!isValidContext(parsed)) {
      this.clear();
      return null;
    }

    return { paymentId: parsed.paymentId, reservationId: parsed.reservationId };
  }

  clear(): void {
    try {
      sessionStorage.removeItem(STORAGE_KEY);
    } catch {
      // Nothing to clean up if storage is unavailable.
    }
  }
}
