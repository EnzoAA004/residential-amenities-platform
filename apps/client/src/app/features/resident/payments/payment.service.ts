import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { ApiClient } from '../../../core/api/api-client.service';
import { apiPaths } from '../../../core/api/api-paths';
import { Reservation } from '../reservations/reservation.models';
import { DeclareCashResponse, InitiateMercadoPagoResponse, Payment } from './payment.models';

/**
 * Thin wrapper over `ApiClient` for the payment flow (issue #49): loading
 * the reservation being paid, initiating Mercado Pago, declaring cash, and
 * reading a payment's real state. No business logic lives here — every
 * decision (whether a reservation is payable, whether a method conflicts
 * with an existing one, what a payment's status means) is the backend's.
 */
@Injectable({
  providedIn: 'root'
})
export class PaymentService {
  private readonly api = inject(ApiClient);

  getReservation(reservationId: string): Observable<Reservation> {
    return this.api.get<Reservation>(apiPaths.reservations.byId(reservationId));
  }

  /** Body is `null`: the backend derives amount/currency from the reservation's own snapshot. */
  initiateMercadoPago(reservationId: string): Observable<InitiateMercadoPagoResponse> {
    return this.api.post<InitiateMercadoPagoResponse, null>(
      apiPaths.payments.initiateMercadoPago(reservationId),
      null
    );
  }

  /** Body is `null`. Declaring cash only records intent — it never confirms the reservation. */
  declareCash(reservationId: string): Observable<DeclareCashResponse> {
    return this.api.post<DeclareCashResponse, null>(apiPaths.payments.declareCash(reservationId), null);
  }

  getPayment(paymentId: string): Observable<Payment> {
    return this.api.get<Payment>(apiPaths.payments.byId(paymentId));
  }
}
