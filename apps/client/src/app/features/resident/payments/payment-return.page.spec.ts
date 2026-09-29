import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';

import { API_BASE_URL } from '../../../core/config/api-base-url.token';
import { MercadoPagoReturnContextStore } from './mercado-pago-return-context.store';
import { Payment } from './payment.models';
import { PaymentReturnPage } from './payment-return.page';

const approvedPayment: Payment = {
  paymentId: 'good-payment',
  reservationId: 'reservation-1',
  method: 'MercadoPago',
  status: 'Approved',
  amount: 5000,
  currency: 'ARS',
  approvedAtUtc: '2026-10-01T13:05:00Z',
  reservationOutcome: 'ReservationConfirmed',
  requiresManualReview: false,
  cashConfirmedAtUtc: null
};

describe('PaymentReturnPage', () => {
  let httpMock: HttpTestingController;

  beforeEach(() => {
    sessionStorage.clear();

    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([{ path: 'payments/return', component: PaymentReturnPage }]),
        { provide: API_BASE_URL, useValue: '/api' }
      ]
    });

    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
    sessionStorage.clear();
  });

  function text(harness: RouterTestingHarness): string {
    return harness.routeNativeElement!.textContent!;
  }

  it('shows "no payment found" when there is no saved return context, and requests nothing', async () => {
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/payments/return', PaymentReturnPage);
    harness.detectChanges();

    expect(text(harness)).toContain('No encontramos un pago iniciado en esta sesión.');
    httpMock.expectNone(() => true);
  });

  it('queries only the paymentId saved before redirecting, even when the URL carries a different, attacker-supplied paymentId', async () => {
    TestBed.inject(MercadoPagoReturnContextStore).save({
      paymentId: 'good-payment',
      reservationId: 'reservation-1'
    });

    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl(
      '/payments/return?status=approved&paymentId=attacker-id&collection_status=approved',
      PaymentReturnPage
    );
    await harness.fixture.whenStable();
    harness.detectChanges();

    // Exactly one request, and it is for the stored id — never the query string's.
    const request = httpMock.expectOne('/api/payments/good-payment');
    httpMock.expectNone('/api/payments/attacker-id');

    // Before the backend answers, the page must not have already rendered
    // "Approved" from the URL's own `status=approved`.
    expect(text(harness)).not.toContain('Aprobado');
    expect(text(harness)).not.toContain('Pago aprobado y reserva confirmada.');

    request.flush(approvedPayment);
    await harness.fixture.whenStable();
    harness.detectChanges();

    expect(text(harness)).toContain('Pago aprobado y reserva confirmada.');
  });

  it('ignores query params entirely — the same behavior with or without them', async () => {
    TestBed.inject(MercadoPagoReturnContextStore).save({
      paymentId: 'good-payment',
      reservationId: 'reservation-1'
    });

    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/payments/return', PaymentReturnPage);
    await harness.fixture.whenStable();
    harness.detectChanges();

    httpMock.expectOne('/api/payments/good-payment').flush(approvedPayment);
    await harness.fixture.whenStable();
    harness.detectChanges();

    expect(text(harness)).toContain('Pago aprobado y reserva confirmada.');
  });

  it('shows the loading state while the GET is in flight', async () => {
    TestBed.inject(MercadoPagoReturnContextStore).save({
      paymentId: 'good-payment',
      reservationId: 'reservation-1'
    });

    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/payments/return', PaymentReturnPage);
    harness.detectChanges();

    expect(text(harness)).toContain('Consultando el pago');
    httpMock.expectOne('/api/payments/good-payment').flush(approvedPayment);
  });

  it('shows a real error and lets the resident refresh manually', async () => {
    TestBed.inject(MercadoPagoReturnContextStore).save({
      paymentId: 'good-payment',
      reservationId: 'reservation-1'
    });

    const harness = await RouterTestingHarness.create();
    const component = await harness.navigateByUrl('/payments/return', PaymentReturnPage);
    httpMock
      .expectOne('/api/payments/good-payment')
      .flush({ status: 404, title: 'Payment not found.' }, { status: 404, statusText: 'Not Found' });
    await harness.fixture.whenStable();
    harness.detectChanges();

    expect(text(harness)).toContain('Payment not found.');

    component.refresh();
    httpMock.expectOne('/api/payments/good-payment').flush(approvedPayment);
    await harness.fixture.whenStable();
    harness.detectChanges();

    expect(text(harness)).toContain('Pago aprobado y reserva confirmada.');
  });

  it.each([
    ['Created', 'Iniciando'],
    ['Pending', 'Pendiente'],
    ['Rejected', 'Rechazado'],
    ['Cancelled', 'Cancelado']
  ] as const)('renders the real %s status safely, without claiming it is paid/confirmed', async (status, label) => {
    TestBed.inject(MercadoPagoReturnContextStore).save({
      paymentId: 'good-payment',
      reservationId: 'reservation-1'
    });

    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/payments/return', PaymentReturnPage);
    httpMock.expectOne('/api/payments/good-payment').flush({
      ...approvedPayment,
      status,
      approvedAtUtc: null,
      reservationOutcome: 'None'
    });
    await harness.fixture.whenStable();
    harness.detectChanges();

    expect(text(harness)).toContain(label);
    expect(text(harness)).not.toContain('Pago aprobado y reserva confirmada.');
  });

  it('shows a manual-review warning for an Approved payment that did not confirm the reservation', async () => {
    TestBed.inject(MercadoPagoReturnContextStore).save({
      paymentId: 'good-payment',
      reservationId: 'reservation-1'
    });

    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/payments/return', PaymentReturnPage);
    httpMock.expectOne('/api/payments/good-payment').flush({
      ...approvedPayment,
      reservationOutcome: 'ApprovedForCancelledReservation',
      requiresManualReview: true
    });
    await harness.fixture.whenStable();
    harness.detectChanges();

    expect(text(harness)).toContain('requiere revisión manual');
    expect(text(harness)).toContain('la reserva ya estaba cancelada');
    expect(text(harness)).not.toContain('Pago aprobado y reserva confirmada.');
  });
});
