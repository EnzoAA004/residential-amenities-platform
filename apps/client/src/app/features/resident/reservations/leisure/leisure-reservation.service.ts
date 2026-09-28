import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { ApiClient } from '../../../../core/api/api-client.service';
import { apiPaths } from '../../../../core/api/api-paths';
import {
  CreateLeisureReservationRequest,
  LeisureUseType,
  PriceQuote,
  Reservation
} from './leisure-reservation.models';

/**
 * Thin wrapper over `ApiClient` for quoting and creating a Leisure
 * reservation. Never computes a price or a conflict client-side — it only
 * sends the request and returns exactly what the backend responds with.
 */
@Injectable({
  providedIn: 'root'
})
export class LeisureReservationService {
  private readonly api = inject(ApiClient);

  getQuote(buildingId: string, amenityId: string, useType: LeisureUseType): Observable<PriceQuote> {
    return this.api.get<PriceQuote>(apiPaths.pricing.quote, { buildingId, amenityId, useType });
  }

  create(request: CreateLeisureReservationRequest): Observable<Reservation> {
    return this.api.post<Reservation, CreateLeisureReservationRequest>(
      apiPaths.reservations.create,
      request
    );
  }
}
