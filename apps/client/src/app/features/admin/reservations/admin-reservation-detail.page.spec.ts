import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';

import { API_BASE_URL } from '../../../core/config/api-base-url.token';
import { AdminReservationDetail } from '../admin.models';
import { AdminReservationDetailPage } from './admin-reservation-detail.page';

const detail: AdminReservationDetail = {
  reservation: {
    reservationId: 'reservation-1',
    buildingId: 'building-1',
    useType: 'ExclusiveLeisure',
    status: 'Cancelled',
    startsAtUtc: '2026-10-05T10:00:00Z',
    endsAtUtc: '2026-10-05T12:00:00Z',
    createdAtUtc: '2026-10-01T10:00:00Z',
    confirmedAtUtc: null,
    cancelledAtUtc: '2026-10-02T10:00:00Z',
    expiredAtUtc: null,
    expiresAtUtc: '2026-10-01T10:30:00Z',
    cancellationReason: 'Maintenance',
    createdByMembershipId: 'membership-1',
    resources: [{ amenityId: 'amenity-1', isExclusive: true }],
    total: 9000,
    currency: 'ARS'
  },
  priceLines: [{ amenityId: 'amenity-1', componentType: 'Base', currency: 'ARS', amount: 9000 }],
  payments: [
    {
      paymentId: 'payment-1',
      reservationId: 'reservation-1',
      buildingId: 'building-1',
      method: 'Cash',
      status: 'Approved',
      amount: 9000,
      currency: 'ARS',
      createdAtUtc: '2026-10-01T10:05:00Z',
      approvedAtUtc: '2026-10-02T09:00:00Z',
      reservationOutcome: 'ApprovedForCancelledReservation',
      requiresManualReview: true,
      providerStatus: null,
      providerStatusDetail: null,
      cashDeclaredAtUtc: '2026-10-01T10:05:00Z',
      cashConfirmedAtUtc: '2026-10-02T09:00:00Z',
      cashConfirmedByUserId: 'admin-user'
    }
  ],
  requiresFinancialReview: true
};

describe('AdminReservationDetailPage', () => {
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([{ path: 'admin/reservations/:id', component: AdminReservationDetailPage }]),
        { provide: API_BASE_URL, useValue: '/api' }
      ]
    });

    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('loads by route param and renders lifecycle, price lines, payments and review warnings', async () => {
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/admin/reservations/reservation-1', AdminReservationDetailPage);
    httpMock.expectOne('/api/admin/reservations/reservation-1').flush(detail);
    await harness.fixture.whenStable();
    harness.detectChanges();

    const rendered = harness.routeNativeElement!.textContent!;
    expect(rendered).toContain('ExclusiveLeisure · Cancelled');
    expect(rendered).toContain('Maintenance');
    expect(rendered).toContain('Base · 9000 ARS');
    expect(rendered).toContain('Cash · Approved');
    expect(rendered).toContain('ApprovedForCancelledReservation');
    expect(rendered).toContain('Esta reserva requiere revisión financiera');
    expect(rendered).toContain('Este pago requiere revisión administrativa');
    expect(rendered).not.toContain('resolver');
  });

  it('shows backend detail errors without calling mutation endpoints', async () => {
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/admin/reservations/missing', AdminReservationDetailPage);
    httpMock
      .expectOne('/api/admin/reservations/missing')
      .flush({ status: 404, title: 'Reservation not found' }, { status: 404, statusText: 'Not Found' });
    await harness.fixture.whenStable();
    harness.detectChanges();

    expect(harness.routeNativeElement!.textContent!).toContain('Reservation not found');
    httpMock.expectNone((request) => request.method !== 'GET');
  });
});
