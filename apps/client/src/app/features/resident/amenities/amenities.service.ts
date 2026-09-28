import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { ApiClient } from '../../../core/api/api-client.service';
import { apiPaths } from '../../../core/api/api-paths';
import { AmenitySummary, AvailabilityInterval } from './amenities.models';

/**
 * Thin wrapper over `ApiClient` for the resident-facing amenity endpoints.
 * Never calls anything outside `apiPaths.amenities` from this feature, and
 * never recomputes availability client-side — it only requests and returns
 * exactly what the backend sent.
 */
@Injectable({
  providedIn: 'root'
})
export class AmenitiesService {
  private readonly api = inject(ApiClient);

  listForBuilding(buildingId: string): Observable<AmenitySummary[]> {
    return this.api.get<AmenitySummary[]>(apiPaths.amenities.listForBuilding(buildingId));
  }

  getAvailability(amenityId: string, fromUtc: string, toUtc: string): Observable<AvailabilityInterval[]> {
    return this.api.get<AvailabilityInterval[]>(apiPaths.amenities.availability(amenityId), {
      fromUtc,
      toUtc
    });
  }
}
