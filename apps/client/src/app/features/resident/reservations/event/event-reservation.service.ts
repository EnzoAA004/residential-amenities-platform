import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { ApiClient } from '../../../../core/api/api-client.service';
import { apiPaths } from '../../../../core/api/api-paths';
import { PriceQuote, Reservation } from '../reservation.models';
import { CreateEventReservationRequest, EventSlotOccurrence } from './event-reservation.models';

/**
 * Thin wrapper over `ApiClient` for the Event reservation flow (issue #48):
 * discovering a building's configured Event slots for a date (issue #62),
 * quoting, and creating. Never computes a price, a slot occurrence or a
 * conflict client-side.
 */
@Injectable({
  providedIn: 'root'
})
export class EventReservationService {
  private readonly api = inject(ApiClient);

  listSlots(buildingId: string, date: string): Observable<EventSlotOccurrence[]> {
    return this.api.get<EventSlotOccurrence[]>(
      apiPaths.reservations.eventSlotsForBuilding(buildingId),
      { date }
    );
  }

  getQuote(
    buildingId: string,
    baseAmenityId: string,
    addOnAmenityIds: readonly string[]
  ): Observable<PriceQuote> {
    return this.api.get<PriceQuote>(apiPaths.pricing.quote, {
      buildingId,
      amenityId: baseAmenityId,
      useType: 'Event',
      addOnAmenityId: addOnAmenityIds
    });
  }

  create(request: CreateEventReservationRequest): Observable<Reservation> {
    return this.api.post<Reservation, CreateEventReservationRequest>(
      apiPaths.reservations.create,
      request
    );
  }
}
