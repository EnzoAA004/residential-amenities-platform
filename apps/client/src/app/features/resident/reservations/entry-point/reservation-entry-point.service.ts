import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { ApiClient } from '../../../../core/api/api-client.service';
import { apiPaths } from '../../../../core/api/api-paths';
import { ReservationEntryPoint } from './reservation-entry-point.models';

@Injectable({
  providedIn: 'root'
})
export class ReservationEntryPointService {
  private readonly api = inject(ApiClient);

  resolve(token: string): Observable<ReservationEntryPoint> {
    return this.api.get<ReservationEntryPoint>(apiPaths.reservations.entryPoint(token));
  }
}
