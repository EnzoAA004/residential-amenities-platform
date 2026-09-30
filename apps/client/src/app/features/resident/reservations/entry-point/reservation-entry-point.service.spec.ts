import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';

import { API_BASE_URL } from '../../../../core/config/api-base-url.token';
import { ReservationEntryPoint } from './reservation-entry-point.models';
import { ReservationEntryPointService } from './reservation-entry-point.service';

describe('ReservationEntryPointService', () => {
  let service: ReservationEntryPointService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        ReservationEntryPointService,
        { provide: API_BASE_URL, useValue: '/api' }
      ]
    });

    service = TestBed.inject(ReservationEntryPointService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('resolves an opaque token through the backend lookup endpoint', async () => {
    const promise = firstValueFrom(service.resolve('Pilot SUM/QR'));

    const request = httpMock.expectOne('/api/reservation-entry-points/Pilot%20SUM%2FQR');
    expect(request.request.method).toBe('GET');

    const entryPoint: ReservationEntryPoint = {
      token: 'pilot-sum',
      buildingId: 'building-a',
      amenityId: 'sum-1',
      displayName: 'Pilot SUM',
      amenityName: 'SUM',
      amenityKind: 'Sum',
      allowsSharedUse: true,
      allowsExclusiveUse: true,
      suggestedUseType: 'Event'
    };
    request.flush(entryPoint);

    await expect(promise).resolves.toEqual(entryPoint);
  });
});
