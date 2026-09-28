import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';

import { API_BASE_URL } from '../../../core/config/api-base-url.token';
import { AmenitiesService } from './amenities.service';
import { AmenitySummary, AvailabilityInterval } from './amenities.models';

describe('AmenitiesService', () => {
  let service: AmenitiesService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        AmenitiesService,
        { provide: API_BASE_URL, useValue: '/api' }
      ]
    });

    service = TestBed.inject(AmenitiesService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('lists amenities for a building at the correct URL', async () => {
    // Observables from HttpClient are cold: subscribe (via firstValueFrom)
    // before calling expectOne(), otherwise no request has been made yet.
    const promise = firstValueFrom(service.listForBuilding('building-1'));

    const request = httpMock.expectOne('/api/buildings/building-1/amenities');
    expect(request.request.method).toBe('GET');

    const amenities: AmenitySummary[] = [
      { id: 'amenity-1', name: 'Pool', kind: 'Leisure', allowsSharedUse: true, allowsExclusiveUse: false }
    ];
    request.flush(amenities);

    await expect(promise).resolves.toEqual(amenities);
  });

  it('requests availability with fromUtc/toUtc as query params', async () => {
    const promise = firstValueFrom(
      service.getAvailability('amenity-1', '2026-01-01T00:00:00.000Z', '2026-01-08T00:00:00.000Z')
    );

    const request = httpMock.expectOne(
      (req) =>
        req.url === '/api/amenities/amenity-1/availability' &&
        req.params.get('fromUtc') === '2026-01-01T00:00:00.000Z' &&
        req.params.get('toUtc') === '2026-01-08T00:00:00.000Z'
    );
    expect(request.request.method).toBe('GET');

    const intervals: AvailabilityInterval[] = [
      { startUtc: '2026-01-01T10:00:00Z', endUtc: '2026-01-01T13:00:00Z' }
    ];
    request.flush(intervals);

    await expect(promise).resolves.toEqual(intervals);
  });
});
