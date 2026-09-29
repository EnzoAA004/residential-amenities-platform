import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';

import { API_BASE_URL } from '../../../../core/config/api-base-url.token';
import { ResidentReservationService } from './resident-reservation.service';

describe('ResidentReservationService', () => {
  let service: ResidentReservationService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        ResidentReservationService,
        { provide: API_BASE_URL, useValue: '/api' }
      ]
    });

    service = TestBed.inject(ResidentReservationService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('lists resident reservations with only buildingId/page/pageSize', async () => {
    const promise = firstValueFrom(service.list('building-1', 2, 25));

    const request = httpMock.expectOne(
      (req) =>
        req.url === '/api/reservations' &&
        req.params.get('buildingId') === 'building-1' &&
        req.params.get('page') === '2' &&
        req.params.get('pageSize') === '25'
    );
    expect(request.request.method).toBe('GET');
    expect(request.request.params.has('membershipId')).toBe(false);
    expect(request.request.params.has('userId')).toBe(false);
    expect(request.request.params.has('createdByMembershipId')).toBe(false);

    const page = { items: [], page: 2, pageSize: 25, totalCount: 0 };
    request.flush(page);
    await expect(promise).resolves.toEqual(page);
  });

  it('loads detail and payment history from reservation-scoped paths', async () => {
    const detailPromise = firstValueFrom(service.getById('reservation-1'));
    const detailRequest = httpMock.expectOne('/api/reservations/reservation-1');
    expect(detailRequest.request.method).toBe('GET');
    detailRequest.flush({
      id: 'reservation-1',
      buildingId: 'building-1',
      useType: 'SharedLeisure',
      status: 'Pending',
      startsAtUtc: '2026-10-01T13:00:00Z',
      endsAtUtc: '2026-10-01T14:00:00Z',
      createdAtUtc: '2026-10-01T12:30:00Z',
      expiresAtUtc: '2026-10-01T13:00:00Z',
      confirmedAtUtc: null,
      cancelledAtUtc: null,
      expiredAtUtc: null,
      cancellationReason: null,
      resources: [],
      priceLines: [],
      currency: 'ARS',
      totalAmount: 5000
    });
    await detailPromise;

    const paymentsPromise = firstValueFrom(service.getPayments('reservation-1'));
    const paymentsRequest = httpMock.expectOne('/api/reservations/reservation-1/payments');
    expect(paymentsRequest.request.method).toBe('GET');
    paymentsRequest.flush([]);
    await expect(paymentsPromise).resolves.toEqual([]);
  });
});
