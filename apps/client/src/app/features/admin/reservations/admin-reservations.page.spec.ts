import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';

import { API_BASE_URL } from '../../../core/config/api-base-url.token';
import { AdminReservationView } from '../admin.models';
import { AdminReservationsPage } from './admin-reservations.page';

const reservationItem: AdminReservationView = {
  reservation: {
    reservationId: 'reservation-1',
    buildingId: 'building-1',
    useType: 'Event',
    status: 'Confirmed',
    startsAtUtc: '2026-10-05T10:00:00Z',
    endsAtUtc: '2026-10-05T12:00:00Z',
    createdAtUtc: '2026-10-01T10:00:00Z',
    confirmedAtUtc: '2026-10-01T10:05:00Z',
    cancelledAtUtc: null,
    expiredAtUtc: null,
    expiresAtUtc: '2026-10-01T10:30:00Z',
    cancellationReason: null,
    createdByMembershipId: 'membership-1',
    resources: [{ amenityId: 'amenity-1', isExclusive: true }],
    total: 15000,
    currency: 'ARS'
  },
  payments: [],
  requiresFinancialReview: true
};

describe('AdminReservationsPage', () => {
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([{ path: 'admin/reservations', component: AdminReservationsPage }]),
        { provide: API_BASE_URL, useValue: '/api' }
      ]
    });

    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  async function navigate() {
    const harness = await RouterTestingHarness.create();
    const component = await harness.navigateByUrl('/admin/reservations', AdminReservationsPage);
    return { harness, component };
  }

  function text(harness: RouterTestingHarness): string {
    return harness.routeNativeElement!.textContent!;
  }

  it('requests the default page and renders pagination and financial review state', async () => {
    const { harness } = await navigate();
    httpMock.expectOne('/api/admin/reservations?page=1&pageSize=50').flush({
      items: [reservationItem],
      page: 1,
      pageSize: 50,
      totalCount: 75
    });
    await harness.fixture.whenStable();
    harness.detectChanges();

    expect(text(harness)).toContain('Página 1');
    expect(text(harness)).toContain('1 de 75');
    expect(text(harness)).toContain('Event · Confirmed');
    expect(text(harness)).toContain('Requiere revisión financiera');
  });

  it('applies only supported backend filters and resets to page one', async () => {
    const { component } = await navigate();
    httpMock.expectOne('/api/admin/reservations?page=1&pageSize=50').flush({
      items: [],
      page: 1,
      pageSize: 50,
      totalCount: 0
    });

    component.setDraft('buildingId', 'building-1');
    component.setDraft('status', 'Cancelled');
    component.setDraft('useType', 'SharedLeisure');
    component.setDraft('fromUtc', '2026-10-01T00:00:00Z');
    component.setDraft('toUtc', '2026-10-02T00:00:00Z');
    component.setDraft('membershipId', 'membership-1');
    component.setDraft('pageSize', 100);
    component.applyFilters(new Event('submit'));

    const request = httpMock.expectOne((request) => request.url === '/api/admin/reservations');
    expect(request.request.params.get('buildingId')).toBe('building-1');
    expect(request.request.params.get('status')).toBe('Cancelled');
    expect(request.request.params.get('useType')).toBe('SharedLeisure');
    expect(request.request.params.get('fromUtc')).toBe('2026-10-01T00:00:00Z');
    expect(request.request.params.get('toUtc')).toBe('2026-10-02T00:00:00Z');
    expect(request.request.params.get('membershipId')).toBe('membership-1');
    expect(request.request.params.get('page')).toBe('1');
    expect(request.request.params.get('pageSize')).toBe('100');
    request.flush({ items: [], page: 1, pageSize: 100, totalCount: 0 });
  });

  it('shows empty and error states', async () => {
    const { harness, component } = await navigate();
    httpMock.expectOne('/api/admin/reservations?page=1&pageSize=50').flush({
      items: [],
      page: 1,
      pageSize: 50,
      totalCount: 0
    });
    await harness.fixture.whenStable();
    harness.detectChanges();

    expect(text(harness)).toContain('No hay reservas');

    component.reload();
    httpMock
      .expectOne('/api/admin/reservations?page=1&pageSize=50')
      .flush({ status: 500, title: 'Backend detail' }, { status: 500, statusText: 'Error' });
    await harness.fixture.whenStable();
    harness.detectChanges();

    expect(text(harness)).toContain('Backend detail');
  });

  it('cancels stale requests when filters change quickly', async () => {
    const { harness, component } = await navigate();
    const stale = httpMock.expectOne('/api/admin/reservations?page=1&pageSize=50');

    component.setDraft('status', 'Pending');
    component.applyFilters(new Event('submit'));
    const fresh = httpMock.expectOne((request) => request.url === '/api/admin/reservations');
    expect(fresh.request.params.get('status')).toBe('Pending');
    expect(fresh.request.params.get('page')).toBe('1');
    expect(fresh.request.params.get('pageSize')).toBe('50');

    expect(() =>
      stale.flush({ items: [reservationItem], page: 1, pageSize: 50, totalCount: 1 })
    ).toThrow();

    fresh.flush({ items: [], page: 1, pageSize: 50, totalCount: 0 });
    await harness.fixture.whenStable();
    harness.detectChanges();

    expect(text(harness)).toContain('No hay reservas');
    expect(text(harness)).not.toContain('Event · Confirmed');
  });
});
