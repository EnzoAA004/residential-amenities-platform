import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { API_BASE_URL } from '../../../core/config/api-base-url.token';
import { Reservation } from '../reservations/reservation.models';
import { ExternalNavigationService } from './external-navigation.service';
import { Payment } from './payment.models';
import { PaymentPage } from './payment.page';

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
    resources: [],
    priceLines: [],
    currency: 'ARS',
    totalAmount: 5000,
    ...overrides
  };
}

const pendingCashPayment: Payment = {
  paymentId: 'payment-2',
  reservationId: 'reservation-1',
  method: 'Cash',
  status: 'Pending',
  amount: 5000,
  currency: 'ARS',
  approvedAtUtc: null,
  reservationOutcome: 'None',
  requiresManualReview: false,
  cashConfirmedAtUtc: null
};

const mercadoPagoInitiateResponse = {
  paymentId: 'payment-1',
  providerOrderId: 'order-1',
  checkoutUrl: 'https://mercadopago.test/checkout/order-1',
  reservationExpiresAtUtc: '2026-10-01T13:00:00Z'
};

describe('PaymentPage', () => {
  let httpMock: HttpTestingController;
  let navigateTo: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    sessionStorage.clear();
    navigateTo = vi.fn();

    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([{ path: 'reservations/:reservationId/payment', component: PaymentPage }]),
        { provide: API_BASE_URL, useValue: '/api' },
        { provide: ExternalNavigationService, useValue: { navigateTo } }
      ]
    });

    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
    sessionStorage.clear();
    vi.useRealTimers();
  });

  async function navigateToPayment(
    reservationId = 'reservation-1'
  ): Promise<{ harness: RouterTestingHarness; component: PaymentPage }> {
    const harness = await RouterTestingHarness.create();
    const component = await harness.navigateByUrl(`/reservations/${reservationId}/payment`, PaymentPage);
    return { harness, component };
  }

  function text(harness: RouterTestingHarness): string {
    return harness.routeNativeElement!.textContent!;
  }

  // --- loading the reservation ---------------------------------------------------

  it('loads the reservation from the route param and shows its snapshot', async () => {
    const { harness } = await navigateToPayment();
    httpMock.expectOne('/api/reservations/reservation-1').flush(reservationWith());
    await harness.fixture.whenStable();
    harness.detectChanges();

    expect(text(harness)).toContain('SharedLeisure');
    expect(text(harness)).toContain('Pending');
    expect(text(harness)).toContain('Pagar con Mercado Pago');
    expect(text(harness)).toContain('Declarar efectivo');
  });

  it('shows a real 403/404/network error without inventing a state', async () => {
    const { harness } = await navigateToPayment();
    httpMock
      .expectOne('/api/reservations/reservation-1')
      .flush(
        { status: 403, title: 'You do not have access to this payment.' },
        { status: 403, statusText: 'Forbidden' }
      );
    await harness.fixture.whenStable();
    harness.detectChanges();

    expect(text(harness)).toContain('You do not have access to this payment.');
  });

  it('does not offer payment actions for a reservation that is not Pending', async () => {
    const { harness } = await navigateToPayment();
    httpMock.expectOne('/api/reservations/reservation-1').flush(reservationWith({ status: 'Confirmed' }));
    await harness.fixture.whenStable();
    harness.detectChanges();

    expect(text(harness)).toContain('no está pendiente de pago');
    expect(text(harness)).not.toContain('Pagar con Mercado Pago');
    expect(text(harness)).not.toContain('Declarar efectivo');
  });

  it('shows a hold countdown from the real expiresAtUtc, and a safe zero-state message — never a client-side Expired status', async () => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date('2026-10-01T12:59:00Z'));

    const { harness } = await navigateToPayment();
    httpMock.expectOne('/api/reservations/reservation-1').flush(reservationWith());
    await harness.fixture.whenStable();
    harness.detectChanges();

    expect(text(harness)).toContain('01:00');

    vi.setSystemTime(new Date('2026-10-01T13:00:30Z'));
    vi.advanceTimersByTime(1000);
    harness.detectChanges();

    expect(text(harness)).toContain('El plazo indicado llegó a cero');
    expect(text(harness)).toContain('Pending');
  });

  // --- Mercado Pago ---------------------------------------------------------------

  it('initiates Mercado Pago with an empty body, saves only paymentId/reservationId, then navigates to checkoutUrl', async () => {
    const { harness, component } = await navigateToPayment();
    httpMock.expectOne('/api/reservations/reservation-1').flush(reservationWith());
    await harness.fixture.whenStable();
    harness.detectChanges();

    component.initiateMercadoPago();

    const request = httpMock.expectOne('/api/reservations/reservation-1/payments/mercadopago');
    expect(request.request.body).toBeNull();

    request.flush(mercadoPagoInitiateResponse);
    await harness.fixture.whenStable();

    const stored = JSON.parse(sessionStorage.getItem('residential-amenities:mercadopago-return')!);
    expect(stored).toEqual({ paymentId: 'payment-1', reservationId: 'reservation-1' });
    expect(navigateTo).toHaveBeenCalledExactlyOnceWith('https://mercadopago.test/checkout/order-1');
  });

  it('never stores the checkoutUrl or providerOrderId', async () => {
    const { harness, component } = await navigateToPayment();
    httpMock.expectOne('/api/reservations/reservation-1').flush(reservationWith());
    await harness.fixture.whenStable();
    harness.detectChanges();

    component.initiateMercadoPago();
    httpMock.expectOne('/api/reservations/reservation-1/payments/mercadopago').flush(mercadoPagoInitiateResponse);
    await harness.fixture.whenStable();

    const raw = sessionStorage.getItem('residential-amenities:mercadopago-return')!;
    expect(raw).not.toContain('checkoutUrl');
    expect(raw).not.toContain('order-1');
    expect(raw).not.toContain('mercadopago.test');
  });

  it.each([409, 422, 502])(
    'shows the real title/detail for a Mercado Pago %i, and allows an immediate manual retry',
    async (status) => {
      const { harness, component } = await navigateToPayment();
      httpMock.expectOne('/api/reservations/reservation-1').flush(reservationWith());
      await harness.fixture.whenStable();
      harness.detectChanges();

      component.initiateMercadoPago();
      httpMock
        .expectOne('/api/reservations/reservation-1/payments/mercadopago')
        .flush(
          { status, title: 'Unable to start this payment.', detail: 'backend detail' },
          { status, statusText: 'Error' }
        );
      await harness.fixture.whenStable();
      harness.detectChanges();

      expect(text(harness)).toContain('Unable to start this payment.');
      expect(text(harness)).toContain('backend detail');
      expect(component.isSubmitting()).toBe(false);
    }
  );

  it('a network failure (status 0) on Mercado Pago initiation allows an immediate manual retry, not a permanent block', async () => {
    const { harness, component } = await navigateToPayment();
    httpMock.expectOne('/api/reservations/reservation-1').flush(reservationWith());
    await harness.fixture.whenStable();
    harness.detectChanges();

    component.initiateMercadoPago();
    httpMock
      .expectOne('/api/reservations/reservation-1/payments/mercadopago')
      .error(new ProgressEvent('error'), { status: 0, statusText: 'Unknown Error' });
    await harness.fixture.whenStable();

    expect(component.isSubmitting()).toBe(false);

    component.initiateMercadoPago();
    httpMock.expectOne('/api/reservations/reservation-1/payments/mercadopago').flush(mercadoPagoInitiateResponse);
    await harness.fixture.whenStable();

    expect(navigateTo).toHaveBeenCalledOnce();
  });

  it('does not send a second Mercado Pago POST for a rapid double click', async () => {
    const { harness, component } = await navigateToPayment();
    httpMock.expectOne('/api/reservations/reservation-1').flush(reservationWith());
    await harness.fixture.whenStable();
    harness.detectChanges();

    component.initiateMercadoPago();
    component.initiateMercadoPago();

    httpMock.expectOne('/api/reservations/reservation-1/payments/mercadopago').flush(mercadoPagoInitiateResponse);
    await harness.fixture.whenStable();
  });

  it('blocks declaring cash while a Mercado Pago initiation is in flight', async () => {
    const { harness, component } = await navigateToPayment();
    httpMock.expectOne('/api/reservations/reservation-1').flush(reservationWith());
    await harness.fixture.whenStable();
    harness.detectChanges();

    component.initiateMercadoPago();
    expect(component.isSubmitting()).toBe(true);

    component.declareCash();
    httpMock.expectNone('/api/reservations/reservation-1/payments/cash');

    httpMock.expectOne('/api/reservations/reservation-1/payments/mercadopago').flush(mercadoPagoInitiateResponse);
    await harness.fixture.whenStable();
  });

  // --- cash -------------------------------------------------------------------

  it('declares cash with an empty body, then fetches and renders the full payment read model', async () => {
    const { harness, component } = await navigateToPayment();
    httpMock.expectOne('/api/reservations/reservation-1').flush(reservationWith());
    await harness.fixture.whenStable();
    harness.detectChanges();

    component.declareCash();
    const declareRequest = httpMock.expectOne('/api/reservations/reservation-1/payments/cash');
    expect(declareRequest.request.body).toBeNull();
    declareRequest.flush({
      paymentId: 'payment-2',
      status: 'Pending',
      reservationExpiresAtUtc: '2026-10-01T13:00:00Z'
    });
    await harness.fixture.whenStable();

    httpMock.expectOne('/api/payments/payment-2').flush(pendingCashPayment);
    await harness.fixture.whenStable();
    harness.detectChanges();

    expect(text(harness)).toContain('Efectivo declarado');
    expect(text(harness)).toContain('Pendiente');
    expect(text(harness)).toContain('todavía NO confirma la reserva');

    // The resident-facing flow must never call the admin-only confirm endpoint.
    httpMock.expectNone((req) => req.url.includes('/cash/confirm'));
  });

  it('shows the confirmed cash outcome once the admin has confirmed receipt, on manual refresh', async () => {
    const { harness, component } = await navigateToPayment();
    httpMock.expectOne('/api/reservations/reservation-1').flush(reservationWith());
    await harness.fixture.whenStable();
    harness.detectChanges();

    component.declareCash();
    httpMock
      .expectOne('/api/reservations/reservation-1/payments/cash')
      .flush({ paymentId: 'payment-2', status: 'Pending', reservationExpiresAtUtc: '2026-10-01T13:00:00Z' });
    await harness.fixture.whenStable();
    httpMock.expectOne('/api/payments/payment-2').flush(pendingCashPayment);
    await harness.fixture.whenStable();
    harness.detectChanges();

    component.refreshCashPayment();
    httpMock.expectOne('/api/payments/payment-2').flush({
      ...pendingCashPayment,
      status: 'Approved',
      approvedAtUtc: '2026-10-05T10:00:00Z',
      reservationOutcome: 'ReservationConfirmed',
      cashConfirmedAtUtc: '2026-10-05T10:00:00Z'
    });
    await harness.fixture.whenStable();
    harness.detectChanges();

    expect(text(harness)).toContain('Pago aprobado y reserva confirmada.');
  });

  it('shows a manual-review warning for an Approved cash payment that could not confirm the reservation', async () => {
    const { harness, component } = await navigateToPayment();
    httpMock.expectOne('/api/reservations/reservation-1').flush(reservationWith());
    await harness.fixture.whenStable();
    harness.detectChanges();

    component.declareCash();
    httpMock
      .expectOne('/api/reservations/reservation-1/payments/cash')
      .flush({ paymentId: 'payment-2', status: 'Pending', reservationExpiresAtUtc: '2026-10-01T13:00:00Z' });
    await harness.fixture.whenStable();
    httpMock.expectOne('/api/payments/payment-2').flush({
      ...pendingCashPayment,
      status: 'Approved',
      approvedAtUtc: '2026-10-05T10:00:00Z',
      reservationOutcome: 'ApprovedAfterExpiry',
      requiresManualReview: true,
      cashConfirmedAtUtc: '2026-10-05T10:00:00Z'
    });
    await harness.fixture.whenStable();
    harness.detectChanges();

    expect(text(harness)).toContain('requiere revisión manual');
    expect(text(harness)).toContain('venciera el plazo de la reserva');
  });

  it('shows a 409 when a Mercado Pago payment is already active, without changing state silently', async () => {
    const { harness, component } = await navigateToPayment();
    httpMock.expectOne('/api/reservations/reservation-1').flush(reservationWith());
    await harness.fixture.whenStable();
    harness.detectChanges();

    component.declareCash();
    httpMock
      .expectOne('/api/reservations/reservation-1/payments/cash')
      .flush(
        { status: 409, title: 'This reservation already has an active MercadoPago payment.' },
        { status: 409, statusText: 'Conflict' }
      );
    await harness.fixture.whenStable();
    harness.detectChanges();

    expect(text(harness)).toContain('This reservation already has an active MercadoPago payment.');
  });

  it('shows the real 403 detail when a resident who did not create the reservation tries to declare cash', async () => {
    const { harness, component } = await navigateToPayment();
    httpMock.expectOne('/api/reservations/reservation-1').flush(reservationWith());
    await harness.fixture.whenStable();
    harness.detectChanges();

    component.declareCash();
    httpMock
      .expectOne('/api/reservations/reservation-1/payments/cash')
      .flush(
        { status: 403, title: 'Only the resident who created the reservation can declare its payment.' },
        { status: 403, statusText: 'Forbidden' }
      );
    await harness.fixture.whenStable();
    harness.detectChanges();

    expect(text(harness)).toContain(
      'Only the resident who created the reservation can declare its payment.'
    );
  });

  it('does not send a second cash POST for a rapid double click', async () => {
    const { harness, component } = await navigateToPayment();
    httpMock.expectOne('/api/reservations/reservation-1').flush(reservationWith());
    await harness.fixture.whenStable();
    harness.detectChanges();

    component.declareCash();
    component.declareCash();

    httpMock
      .expectOne('/api/reservations/reservation-1/payments/cash')
      .flush({ paymentId: 'payment-2', status: 'Pending', reservationExpiresAtUtc: '2026-10-01T13:00:00Z' });
    await harness.fixture.whenStable();
    httpMock.expectOne('/api/payments/payment-2').flush(pendingCashPayment);
  });

  it('a stale cash-payment GET never overwrites a fresher one (refresh race)', async () => {
    const { harness, component } = await navigateToPayment();
    httpMock.expectOne('/api/reservations/reservation-1').flush(reservationWith());
    await harness.fixture.whenStable();
    harness.detectChanges();

    component.declareCash();
    httpMock
      .expectOne('/api/reservations/reservation-1/payments/cash')
      .flush({ paymentId: 'payment-2', status: 'Pending', reservationExpiresAtUtc: '2026-10-01T13:00:00Z' });
    await harness.fixture.whenStable();

    const staleRequest = httpMock.expectOne('/api/payments/payment-2');

    component.refreshCashPayment();
    const freshRequest = httpMock.expectOne('/api/payments/payment-2');

    // switchMap cancels the stale GET outright.
    expect(() => staleRequest.flush(pendingCashPayment)).toThrow();

    freshRequest.flush({
      ...pendingCashPayment,
      status: 'Approved',
      approvedAtUtc: '2026-10-05T10:00:00Z',
      reservationOutcome: 'ReservationConfirmed',
      cashConfirmedAtUtc: '2026-10-05T10:00:00Z'
    });
    await harness.fixture.whenStable();
    harness.detectChanges();

    expect(text(harness)).toContain('Pago aprobado y reserva confirmada.');
  });
});
