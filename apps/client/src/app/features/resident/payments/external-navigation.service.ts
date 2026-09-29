import { Injectable } from '@angular/core';

/**
 * Encapsulates navigating away from the app entirely (Mercado Pago's
 * Checkout Pro). A thin, injectable wrapper around `window.location.assign`
 * so `PaymentPage`'s tests can substitute a fake instead of touching the
 * real browser location.
 *
 * Uses the same-tab navigation deliberately (not `window.open`/`target=_blank`):
 * `sessionStorage` (where `MercadoPagoReturnContextStore` keeps the return
 * correlation) is per-tab, so the checkout must happen in the same
 * navigation context for the return page to find it.
 */
@Injectable({
  providedIn: 'root'
})
export class ExternalNavigationService {
  navigateTo(url: string): void {
    window.location.assign(url);
  }
}
