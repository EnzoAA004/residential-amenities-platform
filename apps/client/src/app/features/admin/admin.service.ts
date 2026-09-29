import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { ApiClient } from '../../core/api/api-client.service';
import { ApiQueryParams } from '../../core/api/api-query-params';
import { apiPaths } from '../../core/api/api-paths';
import {
  AdminCancelReservationRequest,
  AdminPaymentFilters,
  AdminPaymentPage,
  AdminReservationDetail,
  AdminReservationFilters,
  AdminReservationPage,
  AdminRescheduleReservationRequest,
  AuditFilters,
  AuditPage,
  ConfirmCashPaymentResponse
} from './admin.models';

@Injectable({
  providedIn: 'root'
})
export class AdminService {
  private readonly api = inject(ApiClient);

  listReservations(filters: AdminReservationFilters): Observable<AdminReservationPage> {
    return this.api.get<AdminReservationPage>(
      apiPaths.admin.reservations.list,
      compactParams(filters)
    );
  }

  getReservation(reservationId: string): Observable<AdminReservationDetail> {
    return this.api.get<AdminReservationDetail>(apiPaths.admin.reservations.byId(reservationId));
  }

  listPayments(filters: AdminPaymentFilters): Observable<AdminPaymentPage> {
    return this.api.get<AdminPaymentPage>(apiPaths.admin.payments.list, compactParams(filters));
  }

  // (#52) Both mutations return the updated `AdminReservationDetail` — the
  // caller applies that response directly and never issues a follow-up GET.
  cancelReservation(
    reservationId: string,
    request: AdminCancelReservationRequest
  ): Observable<AdminReservationDetail> {
    return this.api.post<AdminReservationDetail>(
      apiPaths.admin.reservations.cancel(reservationId),
      request
    );
  }

  rescheduleReservation(
    reservationId: string,
    request: AdminRescheduleReservationRequest
  ): Observable<AdminReservationDetail> {
    return this.api.post<AdminReservationDetail>(
      apiPaths.admin.reservations.reschedule(reservationId),
      request
    );
  }

  listAudit(filters: AuditFilters): Observable<AuditPage> {
    return this.api.get<AuditPage>(apiPaths.admin.audit.list, compactParams(filters));
  }

  /**
   * Administrative confirmation that cash for a payment was physically
   * received. Reuses the same `POST /payments/{paymentId}/cash/confirm`
   * endpoint the resident-facing flow (#49) already calls — there is no
   * separate `/admin/payments/.../confirm` route, and this method never
   * invents one. Body is `null`: the backend derives everything from the
   * payment itself and the authenticated Administrator.
   */
  confirmCashPayment(paymentId: string): Observable<ConfirmCashPaymentResponse> {
    return this.api.post<ConfirmCashPaymentResponse, null>(
      apiPaths.payments.confirmCash(paymentId),
      null
    );
  }
}

function compactParams(source: object): ApiQueryParams {
  return Object.fromEntries(
    Object.entries(source).filter(([, value]) => value !== undefined && value !== null && value !== '')
  ) as ApiQueryParams;
}
