import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { API_BASE_URL } from '../../../../core/config/api-base-url.token';
import { Reservation } from '../reservation.models';
import { ResidentReservationDetailPage } from './resident-reservation-detail.page';
import { ResidentReservationPayment } from './resident-reservation.models';

function reservationWith(overrides: Partial<Reservation> = {}): Reservation {
  return {
    id: 'reservation-1',
    buildingId: 'building-a',
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
    resources: [{ amenityId: 'amenity-1', isExclusive: false }],
    priceLines: [{ amenityId: 'amenity-1', componentType: 'Base', currency: 'ARS', amount: 5000 }],
    currency: 'ARS',
    totalAmount: 5000,
    ...overrides
  };
}

function paymentWith(overrides: Partial<ResidentReservationPayment> = {}): ResidentReservationPayment {
  return {
    paymentId: 'payment-1',
    reservationId: 'reservation-1',
    method: 'Cash',
    status: 'Pending',
    amount: 5000,
    currency: 'ARS',
    createdAtUtc: '2026-10-01T12:35:00Z',
    approvedAtUtc: null,
    reservationOutcome: 'None',
    requiresManualReview: false,
    cashConfirmedAtUtc: null,
    ...overrides
  };
}

describe('ResidentReservationDetailPage', () => {
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([
          { path: 'reservations/:reservationId', component: ResidentReservationDetailPage }
        ]),
        { provide: API_BASE_URL, useValue: '/api' }
      ]
    });

    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
    sessionStorage.clear();
    vi.useRealTimers();
  });

  async function navigateToDetail(
    reservationId = 'reservation-1'
  ): Promise<{ harness: RouterTestingHarness; component: ResidentReservationDetailPage }> {
    const harness = await RouterTestingHarness.create();
    const component = await harness.navigateByUrl(
      `/reservations/${reservationId}`,
      ResidentReservationDetailPage
    );
    return { harness, component };
  }

  function text(harness: RouterTestingHarness): string {
    return harness.routeNativeElement!.textContent!;
  }

  it('loads detail and payment history by route param, supporting direct navigation', async () => {
    const { harness } = await navigateToDetail('reservation-A');

    httpMock.expectOne('/api/reservations/reservation-A').flush(reservationWith({ id: 'reservation-A' }));
    httpMock.expectOne('/api/reservations/reservation-A/payments').flush([]);
    await harness.fixture.whenStable();
    harness.detectChanges();

    expect(text(harness)).toContain('SharedLeisure');
    expect(text(harness)).toContain('Historial de pagos');
    expect(text(harness)).toContain('todavía no tiene intentos de pago');
  });

  it('never reads a payment id from sessionStorage for history', async () => {
    sessionStorage.setItem('residential-amenities:mercadopago-return', JSON.stringify({ paymentId: 'payment-x' }));

    await navigateToDetail('reservation-1');

    httpMock.expectOne('/api/reservations/reservation-1').flush(reservationWith());
    httpMock.expectOne('/api/reservations/reservation-1/payments').flush([]);
    httpMock.expectNone('/api/payments/payment-x');
    httpMock.expectNone((req) => req.url.includes('/admin/'));
  });

  it('shows Pending countdown and keeps a zero-state without mutating status', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    vi.setSystemTime(new Date('2026-10-01T12:59:00Z'));

    const { harness } = await navigateToDetail();
    httpMock.expectOne('/api/reservations/reservation-1').flush(reservationWith());
    httpMock.expectOne('/api/reservations/reservation-1/payments').flush([]);
    await harness.fixture.whenStable();
    harness.detectChanges();

    expect(text(harness)).toContain('01:00');

    vi.setSystemTime(new Date('2026-10-01T13:00:30Z'));
    vi.advanceTimersByTime(1000);
    harness.detectChanges();

    expect(text(harness)).toContain('El plazo indicado llegó a cero');
    expect(text(harness)).toContain('Pending');
  });

  it.each([
    ['Confirmed', { confirmedAtUtc: '2026-10-01T12:45:00Z' }],
    ['Expired', { expiredAtUtc: '2026-10-01T13:00:00Z' }],
    ['Cancelled', { cancelledAtUtc: '2026-10-01T12:50:00Z', cancellationReason: 'Resident request' }]
  ])('shows lifecycle state %s', async (status, overrides) => {
    const { harness } = await navigateToDetail();
    httpMock
      .expectOne('/api/reservations/reservation-1')
      .flush(reservationWith({ status, ...overrides }));
    httpMock.expectOne('/api/reservations/reservation-1/payments').flush([]);
    await harness.fixture.whenStable();
    harness.detectChanges();

    expect(text(harness)).toContain(status);
    if (status === 'Cancelled') {
      expect(text(harness)).toContain('Resident request');
    }
  });

  it('renders one payment attempt with all resident-facing fields', async () => {
    const { harness } = await navigateToDetail();
    httpMock.expectOne('/api/reservations/reservation-1').flush(reservationWith());
    httpMock.expectOne('/api/reservations/reservation-1/payments').flush([
      paymentWith({
        status: 'Approved',
        approvedAtUtc: '2026-10-01T12:45:00Z',
        reservationOutcome: 'ReservationConfirmed',
        cashConfirmedAtUtc: '2026-10-01T12:45:00Z'
      })
    ]);
    await harness.fixture.whenStable();
    harness.detectChanges();

    expect(text(harness)).toContain('Cash');
    expect(text(harness)).toContain('Approved');
    expect(text(harness)).toContain('ReservationConfirmed');
    expect(text(harness)).toMatch(/(?:ARS|\$)\s*5[,.]000/);
  });

  it('renders multiple payment attempts, preserving backend order', async () => {
    const { harness } = await navigateToDetail();
    httpMock.expectOne('/api/reservations/reservation-1').flush(reservationWith());
    httpMock.expectOne('/api/reservations/reservation-1/payments').flush([
      paymentWith({ paymentId: 'payment-new', method: 'MercadoPago', status: 'Rejected' }),
      paymentWith({ paymentId: 'payment-old', method: 'Cash', status: 'Pending' })
    ]);
    await harness.fixture.whenStable();
    harness.detectChanges();

    const pageText = text(harness);
    expect(pageText.indexOf('MercadoPago')).toBeLessThan(pageText.indexOf('Cash'));
  });

  it('shows requiresManualReview separately from status and does not present Approved as automatically confirmed', async () => {
    const { harness } = await navigateToDetail();
    httpMock.expectOne('/api/reservations/reservation-1').flush(reservationWith());
    httpMock.expectOne('/api/reservations/reservation-1/payments').flush([
      paymentWith({
        status: 'Approved',
        reservationOutcome: 'ApprovedAfterExpiry',
        requiresManualReview: true,
        approvedAtUtc: '2026-10-01T14:00:00Z'
      })
    ]);
    await harness.fixture.whenStable();
    harness.detectChanges();

    expect(text(harness)).toContain('Approved');
    expect(text(harness)).toContain('Este pago requiere revisión administrativa.');
    expect(text(harness)).toContain('después de que venciera');
    expect(text(harness)).not.toContain('Pago aprobado y reserva confirmada');
  });

  it('shows backend detail errors such as 403 and 404', async () => {
    const { harness } = await navigateToDetail();
    httpMock.expectOne('/api/reservations/reservation-1/payments').flush([]);
    httpMock
      .expectOne('/api/reservations/reservation-1')
      .flush({ status: 404, title: 'Reservation not found.' }, { status: 404, statusText: 'Not Found' });
    await harness.fixture.whenStable();
    harness.detectChanges();

    expect(text(harness)).toContain('Reservation not found.');
  });

  it('cancels stale requests when navigating to another reservation id', async () => {
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/reservations/reservation-A', ResidentReservationDetailPage);

    const staleDetail = httpMock.expectOne('/api/reservations/reservation-A');
    const stalePayments = httpMock.expectOne('/api/reservations/reservation-A/payments');

    await harness.navigateByUrl('/reservations/reservation-B', ResidentReservationDetailPage);
    const freshDetail = httpMock.expectOne('/api/reservations/reservation-B');
    const freshPayments = httpMock.expectOne('/api/reservations/reservation-B/payments');

    expect(() => staleDetail.flush(reservationWith({ id: 'reservation-A', useType: 'Stale' }))).toThrow();
    expect(() => stalePayments.flush([])).toThrow();

    freshDetail.flush(reservationWith({ id: 'reservation-B', useType: 'Fresh' }));
    freshPayments.flush([]);
    await harness.fixture.whenStable();
    harness.detectChanges();

    expect(text(harness)).toContain('Fresh');
    expect(text(harness)).not.toContain('Stale');
  });
});
