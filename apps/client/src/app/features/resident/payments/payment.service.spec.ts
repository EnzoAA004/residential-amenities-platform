import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';

import { API_BASE_URL } from '../../../core/config/api-base-url.token';
import { Reservation } from '../reservations/reservation.models';
import { Payment } from './payment.models';
import { PaymentService } from './payment.service';

describe('PaymentService', () => {
  let service: PaymentService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        PaymentService,
        { provide: API_BASE_URL, useValue: '/api' }
      ]
    });

    service = TestBed.inject(PaymentService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('loads the reservation being paid from the generic reservation endpoint', async () => {
    const promise = firstValueFrom(service.getReservation('reservation-1'));

    const request = httpMock.expectOne('/api/reservations/reservation-1');
    expect(request.request.method).toBe('GET');

    const reservation: Reservation = {
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
    };
    request.flush(reservation);

    await expect(promise).resolves.toEqual(reservation);
  });

  it('initiates Mercado Pago with an empty body and no amount/currency/credentials', async () => {
    const promise = firstValueFrom(service.initiateMercadoPago('reservation-1'));

    const request = httpMock.expectOne('/api/reservations/reservation-1/payments/mercadopago');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toBeNull();

    const response = {
      paymentId: 'payment-1',
      providerOrderId: 'order-1',
      checkoutUrl: 'https://mercadopago.test/checkout/order-1',
      reservationExpiresAtUtc: '2026-10-01T13:00:00Z'
    };
    request.flush(response);

    await expect(promise).resolves.toEqual(response);
  });

  it('declares cash with an empty body', async () => {
    const promise = firstValueFrom(service.declareCash('reservation-1'));

    const request = httpMock.expectOne('/api/reservations/reservation-1/payments/cash');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toBeNull();

    const response = {
      paymentId: 'payment-2',
      status: 'Pending' as const,
      reservationExpiresAtUtc: '2026-10-01T13:00:00Z'
    };
    request.flush(response);

    await expect(promise).resolves.toEqual(response);
  });

  it('reads a payment by id', async () => {
    const promise = firstValueFrom(service.getPayment('payment-1'));

    const request = httpMock.expectOne('/api/payments/payment-1');
    expect(request.request.method).toBe('GET');

    const payment: Payment = {
      paymentId: 'payment-1',
      reservationId: 'reservation-1',
      method: 'MercadoPago',
      status: 'Approved',
      amount: 5000,
      currency: 'ARS',
      approvedAtUtc: '2026-10-01T12:45:00Z',
      reservationOutcome: 'ReservationConfirmed',
      requiresManualReview: false,
      cashConfirmedAtUtc: null
    };
    request.flush(payment);

    await expect(promise).resolves.toEqual(payment);
  });
});
