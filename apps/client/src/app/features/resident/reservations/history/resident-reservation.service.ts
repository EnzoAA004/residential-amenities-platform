import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { ApiClient } from '../../../../core/api/api-client.service';
import { apiPaths } from '../../../../core/api/api-paths';
import { Reservation } from '../reservation.models';
import { ResidentReservationPage, ResidentReservationPayment } from './resident-reservation.models';

@Injectable({
  providedIn: 'root'
})
export class ResidentReservationService {
  private readonly api = inject(ApiClient);

  list(buildingId: string, page: number, pageSize: number): Observable<ResidentReservationPage> {
    return this.api.get<ResidentReservationPage>(apiPaths.reservations.list, {
      buildingId,
      page,
      pageSize
    });
  }

  getById(reservationId: string): Observable<Reservation> {
    return this.api.get<Reservation>(apiPaths.reservations.byId(reservationId));
  }

  getPayments(reservationId: string): Observable<ResidentReservationPayment[]> {
    return this.api.get<ResidentReservationPayment[]>(apiPaths.reservations.payments(reservationId));
  }
}
