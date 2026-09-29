import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';

import { API_BASE_URL } from '../../core/config/api-base-url.token';
import { AdminService } from './admin.service';

describe('AdminService', () => {
  let service: AdminService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: API_BASE_URL, useValue: '/api' }
      ]
    });

    service = TestBed.inject(AdminService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('lists reservations with the exact backend filters and pagination', () => {
    service
      .listReservations({
        buildingId: 'building-1',
        status: 'Confirmed',
        useType: 'Event',
        fromUtc: '2026-10-01T00:00:00Z',
        toUtc: '2026-10-02T00:00:00Z',
        membershipId: 'membership-1',
        page: 2,
        pageSize: 100
      })
      .subscribe();

    const request = httpMock.expectOne(
      '/api/admin/reservations?buildingId=building-1&status=Confirmed&useType=Event&fromUtc=2026-10-01T00:00:00Z&toUtc=2026-10-02T00:00:00Z&membershipId=membership-1&page=2&pageSize=100'
    );
    expect(request.request.method).toBe('GET');
    request.flush({ items: [], page: 2, pageSize: 100, totalCount: 0 });
  });

  it('loads a reservation detail through the read-only admin endpoint', () => {
    service.getReservation('reservation-1').subscribe();

    const request = httpMock.expectOne('/api/admin/reservations/reservation-1');
    expect(request.request.method).toBe('GET');
    request.flush({ reservation: {}, priceLines: [], payments: [], requiresFinancialReview: false });
  });

  it('lists payments with the exact backend filters and pagination', () => {
    service
      .listPayments({
        buildingId: 'building-1',
        method: 'Cash',
        status: 'Approved',
        requiresManualReview: false,
        reservationId: 'reservation-1',
        fromUtc: '2026-10-01T00:00:00Z',
        toUtc: '2026-10-02T00:00:00Z',
        page: 3,
        pageSize: 25
      })
      .subscribe();

    const request = httpMock.expectOne(
      '/api/admin/payments?buildingId=building-1&method=Cash&status=Approved&requiresManualReview=false&reservationId=reservation-1&fromUtc=2026-10-01T00:00:00Z&toUtc=2026-10-02T00:00:00Z&page=3&pageSize=25'
    );
    expect(request.request.method).toBe('GET');
    request.flush({ items: [], page: 3, pageSize: 25, totalCount: 0 });
  });

  it('never calls admin mutation endpoints for read operations', () => {
    service.listReservations({ page: 1, pageSize: 50 }).subscribe();
    httpMock.expectOne('/api/admin/reservations?page=1&pageSize=50').flush({
      items: [],
      page: 1,
      pageSize: 50,
      totalCount: 0
    });

    httpMock.expectNone((request) => request.method !== 'GET');
    httpMock.expectNone((request) => request.url.includes('/cancel'));
    httpMock.expectNone((request) => request.url.includes('/reschedule'));
    httpMock.expectNone((request) => request.url.includes('/cash/confirm'));
  });
});
