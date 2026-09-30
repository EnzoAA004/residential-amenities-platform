import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { ApiClient } from '../../../../core/api/api-client.service';
import { apiPaths } from '../../../../core/api/api-paths';
import { PriceQuote, Reservation } from '../reservation.models';
import {
  CreateLeisureReservationRequest,
  LeisureUseType,
  SharedOccupancy
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

  /**
   * Issue #89: unit labels only (never a name, email, user id or membership
   * id) already booked for this SharedLeisure amenity/period. Informational
   * only — the backend never rejects for capacity.
   */
  getSharedOccupancy(
    buildingId: string,
    amenityId: string,
    startsAtUtc: string,
    endsAtUtc: string
  ): Observable<SharedOccupancy> {
    return this.api.get<SharedOccupancy>(apiPaths.reservations.sharedOccupancy, {
      buildingId,
      amenityId,
      startsAtUtc,
      endsAtUtc
    });
  }
}
