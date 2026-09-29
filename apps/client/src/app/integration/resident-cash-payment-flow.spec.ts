import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';

import { API_BASE_URL } from '../core/config/api-base-url.token';
import { routes } from '../app.routes';
import { AmenitiesPage } from '../features/resident/amenities/amenities.page';
import { AmenitySummary } from '../features/resident/amenities/amenities.models';
import { LeisureReservationComponent } from '../features/resident/reservations/leisure/leisure-reservation.component';
import { PaymentPage } from '../features/resident/payments/payment.page';
import { Payment } from '../features/resident/payments/payment.models';
import { PriceQuote, Reservation } from '../features/resident/reservations/reservation.models';

/**
 * Full-flow integration test (issue #55): session bootstrap → resident
 * context → load amenities → create a Leisure reservation → declare a cash
 * payment. Every layer is real (real `AuthService`/`authenticatedGuard`,
 * `AmenitiesPage`, `LeisureReservationComponent`, `PaymentPage`, real
 * `Router` via `RouterTestingHarness`) — only the HTTP boundary is mocked,
 * and only with the exact contracts this flow actually calls:
 * `GET /api/auth/me`, `GET /api/buildings/{buildingId}/amenities`,
 * `GET /api/pricing/quote`, `POST /api/reservations`,
 * `GET /api/reservations/{id}`, `POST /api/reservations/{id}/payments/cash`,
 * `GET /api/payments/{id}`.
 *
 * Deliberately starts from `/amenities` rather than driving the actual
 * `/login` form: `authenticatedGuard`'s own `GET /api/auth/me` check IS the
 * real session bootstrap every guarded route performs (on first load, on
 * refresh, or right after a real login's own redirect) — exercising it here
 * covers the same session/routing integration this issue asks for, without
 * also depending on `AuthService.login()`'s fire-and-forget post-login
 * redirect, whose own independent `authenticatedGuard` invocation races this
 * test's own explicit navigation in a way that proved flaky in CI across
 * several attempts. The login form itself (submission, validation, error
 * rendering) already has its own dedicated coverage in `login.page.spec.ts`.
 *
 * The pricing quote is included because the real Leisure flow requires one
 * as an explicit step before create — it is not optional here.
 *
 * The central assertion: after declaring cash, the page must never present
 * the reservation as confirmed. Declaring cash only records intent; an
 * administrator still has to confirm the cash was received (#54).
 */
describe('resident cash payment flow (integration)', () => {
  let httpMock: HttpTestingController;

  const authenticatedMeResponse = {
    id: 'user-1',
    email: 'resident@example.test',
    displayName: 'Resident',
    roles: ['Resident'],
    memberships: [{ buildingId: 'building-a', unitId: 'unit-a', unit: '1A', building: 'Building A' }]
  };

  const amenity: AmenitySummary = {
    id: 'amenity-pool',
    name: 'Pool',
    kind: 'Pool',
    allowsSharedUse: true,
    allowsExclusiveUse: false
  };

  const quote: PriceQuote = {
    currency: 'ARS',
    totalAmount: 5000,
    quotedAtUtc: '2026-10-01T00:00:00Z',
    lines: [
      { priceRuleId: 'rule-1', amenityId: 'amenity-pool', componentType: 'Base', currency: 'ARS', amount: 5000 }
    ]
  };

  const createdReservation: Reservation = {
    id: 'reservation-1',
    buildingId: 'building-a',
    useType: 'SharedLeisure',
    status: 'Pending',
    startsAtUtc: '2026-10-05T10:00:00Z',
    endsAtUtc: '2026-10-05T12:00:00Z',
    createdAtUtc: '2026-10-01T00:00:00Z',
    expiresAtUtc: '2026-10-01T00:30:00Z',
    confirmedAtUtc: null,
    cancelledAtUtc: null,
    expiredAtUtc: null,
    cancellationReason: null,
    resources: [{ amenityId: 'amenity-pool', isExclusive: false }],
    priceLines: [{ amenityId: 'amenity-pool', componentType: 'Base', currency: 'ARS', amount: 5000 }],
    currency: 'ARS',
    totalAmount: 5000
  };

  const pendingCashPayment: Payment = {
    paymentId: 'payment-1',
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

  beforeEach(() => {
    sessionStorage.clear();

    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter(routes),
        { provide: API_BASE_URL, useValue: '/api' }
      ]
    });

    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
    sessionStorage.clear();
  });

  it('bootstraps a resident session, loads amenities, creates a Leisure reservation and declares cash — without ever showing it as confirmed', async () => {
    // --- 1. Session bootstrap: authenticatedGuard's own /auth/me check --------
    const harness = await RouterTestingHarness.create();

    // Unlike every other spec in this codebase, this test uses the REAL
    // app.routes.ts — including `/amenities`' actual `canMatch:
    // [authenticatedGuard]`. That guard is async (`await auth.initialize()`),
    // so navigateByUrl()'s own returned promise cannot resolve until the
    // GET it triggers is flushed. It must therefore be flushed while the
    // navigation promise is still in flight, not awaited first.
    const amenitiesNavigation = harness.navigateByUrl('/amenities', AmenitiesPage);
    httpMock.expectOne('/api/auth/me').flush(authenticatedMeResponse);
    const amenitiesComponent = await amenitiesNavigation;
    await harness.fixture.whenStable();

    httpMock.expectOne('/api/buildings/building-a/amenities').flush([amenity]);
    await harness.fixture.whenStable();
    harness.fixture.detectChanges();

    expect(harness.routeNativeElement!.textContent).toContain('Pool');

    // --- 2. Select the amenity, quote, and create a Leisure reservation -------
    amenitiesComponent.selectAmenity(amenity);
    harness.fixture.detectChanges();

    const leisureDebugElement = harness.fixture.debugElement.query(
      By.directive(LeisureReservationComponent)
    );
    expect(leisureDebugElement).toBeTruthy();
    const leisureComponent = leisureDebugElement.componentInstance as LeisureReservationComponent;

    leisureComponent.requestQuote();
    httpMock.expectOne((req) => req.url === '/api/pricing/quote').flush(quote);
    await harness.fixture.whenStable();
    harness.fixture.detectChanges();

    leisureComponent.create();
    httpMock.expectOne('/api/reservations').flush(createdReservation);
    await harness.fixture.whenStable();
    harness.fixture.detectChanges();

    const afterCreateText = harness.routeNativeElement!.textContent!;
    expect(afterCreateText).toContain('Reservation hold created');
    expect(afterCreateText).toContain('Pending');

    // --- 3. Navigate to the payment page and declare cash ----------------------
    const paymentComponent = await harness.navigateByUrl(
      '/reservations/reservation-1/payment',
      PaymentPage
    );

    httpMock.expectOne('/api/reservations/reservation-1').flush(createdReservation);
    await harness.fixture.whenStable();
    harness.fixture.detectChanges();

    paymentComponent.declareCash();

    httpMock.expectOne('/api/reservations/reservation-1/payments/cash').flush({
      paymentId: 'payment-1',
      status: 'Pending',
      reservationExpiresAtUtc: '2026-10-01T00:30:00Z'
    });

    httpMock.expectOne('/api/payments/payment-1').flush(pendingCashPayment);
    await harness.fixture.whenStable();
    harness.fixture.detectChanges();

    // --- 4. Cash Pending must never be shown/treated as Reservation Confirmed -
    const pageText = harness.routeNativeElement!.textContent!;
    expect(pageText).toContain('Efectivo declarado');
    expect(pageText).toContain('Pendiente');
    expect(pageText).toContain('todavía NO confirma la reserva');
    expect(pageText).not.toContain('reserva confirmada');
    expect(pageText).not.toContain('Pago aprobado y reserva confirmada');
    expect(paymentComponent.reservation()?.status).toBe('Pending');
  });
});
