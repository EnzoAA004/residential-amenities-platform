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
  /**
   * Returns `true` once the context is actually persisted, `false` if
   * `sessionStorage` threw (private browsing, disabled storage, quota).
   * The caller (`PaymentPage`) must not redirect to Mercado Pago's checkout
   * on `false`: the backend payment was created successfully, but without a
   * saved correlation id, the return page could never confirm which payment
   * to show.
   */
  save(context: MercadoPagoReturnContext): boolean {
    try {
      sessionStorage.setItem(
        STORAGE_KEY,
        JSON.stringify({ paymentId: context.paymentId, reservationId: context.reservationId })
      );
      return true;
    } catch {
      return false;
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
