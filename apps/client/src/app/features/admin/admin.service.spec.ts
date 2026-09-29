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

  it('lists audit entries with the exact backend filters and pagination', () => {
    service
      .listAudit({
        buildingId: 'building-1',
        actorUserId: 'user-1',
        action: 'CashPaymentConfirmed',
        targetType: 'Payment',
        targetId: 'payment-1',
        fromUtc: '2026-10-01T00:00:00Z',
        toUtc: '2026-10-02T00:00:00Z',
        page: 2,
        pageSize: 100
      })
      .subscribe();

    const request = httpMock.expectOne(
      '/api/admin/audit?buildingId=building-1&actorUserId=user-1&action=CashPaymentConfirmed&targetType=Payment&targetId=payment-1&fromUtc=2026-10-01T00:00:00Z&toUtc=2026-10-02T00:00:00Z&page=2&pageSize=100'
    );
    expect(request.request.method).toBe('GET');
    request.flush({ items: [], page: 2, pageSize: 100, totalCount: 0 });
  });

  it('confirms a cash payment through the shared resident/admin endpoint', () => {
    service.confirmCashPayment('payment-1').subscribe();

    const request = httpMock.expectOne('/api/payments/payment-1/cash/confirm');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toBeNull();
    request.flush({
      paymentId: 'payment-1',
      reservationId: 'reservation-1',
      status: 'Approved',
      amount: 12000,
      currency: 'ARS',
      cashConfirmedAtUtc: '2026-10-01T10:00:00Z',
      cashConfirmedByUserId: 'admin-1',
      reservationOutcome: 'ReservationConfirmed',
      requiresManualReview: false
    });
  });

  it('lists price rules with the exact backend filters', () => {
    service
      .listPriceRules({ buildingId: 'building-1', amenityId: 'amenity-1', page: 1, pageSize: 50 })
      .subscribe();

    const request = httpMock.expectOne(
      '/api/admin/pricing/rules?buildingId=building-1&amenityId=amenity-1&page=1&pageSize=50'
    );
    expect(request.request.method).toBe('GET');
    request.flush({ items: [], page: 1, pageSize: 50, totalCount: 0 });
  });

  it('creates a price rule against the pricing rules endpoint', () => {
    service
      .createPriceRule({
        buildingId: 'building-1',
        amenityId: 'amenity-1',
        componentType: 'Base',
        useType: 'SharedLeisure',
        currency: 'XTS',
        amount: 0.01
      })
      .subscribe();

    const request = httpMock.expectOne('/api/admin/pricing/rules');
    expect(request.request.method).toBe('POST');
    request.flush({
      created: {
        id: 'rule-1',
        buildingId: 'building-1',
        amenityId: 'amenity-1',
        componentType: 'Base',
        useType: 'SharedLeisure',
        currency: 'XTS',
        amount: 0.01,
        effectiveFromUtc: '2026-10-01T00:00:00Z',
        effectiveToUtc: null
      },
      superseded: []
    });
  });

  it('reads availability with the buildingId query param', () => {
    service.getAvailability('amenity-1', 'building-1').subscribe();

    const request = httpMock.expectOne('/api/admin/amenities/amenity-1/availability?buildingId=building-1');
    expect(request.request.method).toBe('GET');
    request.flush({ amenityId: 'amenity-1', buildingId: 'building-1', windows: [], unavailablePeriods: [] });
  });

  it('replaces availability via PUT', () => {
    service
      .replaceAvailability('amenity-1', {
        buildingId: 'building-1',
        windows: [{ dayOfWeek: 1, startTime: '10:00:00', endTime: '12:00:00' }]
      })
      .subscribe();

    const request = httpMock.expectOne('/api/admin/amenities/amenity-1/availability');
    expect(request.request.method).toBe('PUT');
    request.flush({ amenityId: 'amenity-1', buildingId: 'building-1', windows: [], unavailablePeriods: [] });
  });

  it('creates and deletes an unavailable period against the exact endpoints', () => {
    service
      .createUnavailablePeriod('amenity-1', {
        buildingId: 'building-1',
        startsAtUtc: '2026-10-05T09:00:00Z',
        endsAtUtc: '2026-10-05T18:00:00Z'
      })
      .subscribe();

    httpMock.expectOne('/api/admin/amenities/amenity-1/unavailable-periods').flush({ periodId: 'period-1' });

    service.deleteUnavailablePeriod('amenity-1', 'period-1', 'building-1').subscribe();
    const deleteRequest = httpMock.expectOne(
      '/api/admin/amenities/amenity-1/unavailable-periods/period-1?buildingId=building-1'
    );
    expect(deleteRequest.request.method).toBe('DELETE');
    deleteRequest.flush(null, { status: 204, statusText: 'No Content' });
  });

  it('lists, creates, updates and toggles event slots against the exact endpoints', () => {
    service.listEventSlots('building-1').subscribe();
    httpMock.expectOne('/api/admin/buildings/building-1/event-slots').flush([]);

    service.createEventSlot('building-1', { name: 'Example', startTime: '10:00:00', endTime: '11:00:00' }).subscribe();
    httpMock.expectOne((r) => r.url === '/api/admin/buildings/building-1/event-slots' && r.method === 'POST').flush({
      id: 'slot-1',
      buildingId: 'building-1',
      name: 'Example',
      startTime: '10:00:00',
      endTime: '11:00:00',
      isActive: true
    });

    service.updateEventSlot('slot-1', { name: 'Example', startTime: '11:00:00', endTime: '12:00:00' }).subscribe();
    httpMock.expectOne((r) => r.url === '/api/admin/event-slots/slot-1' && r.method === 'PUT').flush({
      id: 'slot-1',
      buildingId: 'building-1',
      name: 'Example',
      startTime: '11:00:00',
      endTime: '12:00:00',
      isActive: true
    });

    service.deactivateEventSlot('slot-1').subscribe();
    httpMock.expectOne('/api/admin/event-slots/slot-1/deactivate').flush({
      id: 'slot-1',
      buildingId: 'building-1',
      name: 'Example',
      startTime: '11:00:00',
      endTime: '12:00:00',
      isActive: false
    });

    service.activateEventSlot('slot-1').subscribe();
    httpMock.expectOne('/api/admin/event-slots/slot-1/activate').flush({
      id: 'slot-1',
      buildingId: 'building-1',
      name: 'Example',
      startTime: '11:00:00',
      endTime: '12:00:00',
      isActive: true
    });

    httpMock.expectNone((r) => r.url.includes('/admin/event-slots') && r.method === 'DELETE');
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
