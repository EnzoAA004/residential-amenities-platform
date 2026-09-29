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
import { LoginPage } from '../features/login/login.page';
import { PaymentPage } from '../features/resident/payments/payment.page';
import { Payment } from '../features/resident/payments/payment.models';
import { PriceQuote, Reservation } from '../features/resident/reservations/reservation.models';

/**
 * Full-flow integration test (issue #55): login → resident session
 * bootstrap → load amenities → create a Leisure reservation → declare a
 * cash payment. Every layer is real (real `AuthService`, `AmenitiesPage`,
 * `LeisureReservationComponent`, `PaymentPage`, real `Router` via
 * `RouterTestingHarness`) — only the HTTP boundary is mocked, and only with
 * the exact contracts this flow actually calls:
 * `POST /api/auth/login`, `GET /api/auth/me`,
 * `GET /api/buildings/{buildingId}/amenities`, `GET /api/pricing/quote`,
 * `POST /api/reservations`, `GET /api/reservations/{id}`,
 * `POST /api/reservations/{id}/payments/cash`, `GET /api/payments/{id}`.
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
    // Final safety net: a stray `/api/auth/me` from a fire-and-forget
    // post-login redirect's own guard check (see `awaitDrainingStrayAuthMe`
    // below) should already have been drained by then, but this guarantees
    // `verify()` is never the one reporting it as an unhandled test failure.
    httpMock.match('/api/auth/me').forEach((request) =>
      request.flush({
        id: 'user-1',
        email: 'resident@example.test',
        displayName: 'Resident',
        roles: ['Resident'],
        memberships: [{ buildingId: 'building-a', unitId: 'unit-a', unit: '1A', building: 'Building A' }]
      })
    );
    httpMock.verify();
    sessionStorage.clear();
  });

  const authenticatedMeResponse = {
    id: 'user-1',
    email: 'resident@example.test',
    displayName: 'Resident',
    roles: ['Resident'],
    memberships: [{ buildingId: 'building-a', unitId: 'unit-a', unit: '1A', building: 'Building A' }]
  };

  /**
   * `login()`'s success handler fires a fire-and-forget post-login redirect
   * (to `/`, via its own `authenticatedGuard`) alongside whatever navigation
   * this test drives explicitly next — the exact tick on which that guard's
   * own `/auth/me` call (if `AuthService.initialize()`'s memoization hasn't
   * settled yet from this guard's perspective) gets dispatched is not
   * something this test can pin down or should have to. Rather than a
   * single flush at a guessed point, this polls for and drains any such
   * stray request on every macrotask tick while `promise` is still pending,
   * so it can never block `promise` (or a later `whenStable()`) regardless
   * of exactly when it appears. Real, explicitly awaited requests — the
   * initial 401 and the post-login 200 — are still asserted and flushed
   * directly with `httpMock.expectOne(...)`, never through this helper.
   */
  async function awaitDrainingStrayAuthMe<T>(promise: Promise<T>): Promise<T> {
    let settled = false;
    promise.then(
      () => {
        settled = true;
      },
      () => {
        settled = true;
      }
    );

    while (!settled) {
      httpMock.match('/api/auth/me').forEach((request) => request.flush(authenticatedMeResponse));
      await new Promise((resolve) => setTimeout(resolve, 0));
    }

    return promise;
  }

  it('logs in, loads amenities, creates a Leisure reservation and declares cash — without ever showing it as confirmed', async () => {
    // --- 1. Login page loads for an anonymous visitor -----------------------
    const harness = await RouterTestingHarness.create();
    const loginComponent = await harness.navigateByUrl('/login', LoginPage);

    httpMock.expectOne('/api/auth/me').flush(
      { status: 401, title: 'You need to sign in to continue.' },
      { status: 401, statusText: 'Unauthorized' }
    );
    await awaitDrainingStrayAuthMe(harness.fixture.whenStable());

    // --- 2. Real login submit -------------------------------------------------
    loginComponent.form.setValue({ email: 'resident@example.test', password: 'Test!Password123' });
    loginComponent.submit();

    httpMock.expectOne('/api/auth/login').flush(null);
    httpMock.expectOne('/api/auth/me').flush(authenticatedMeResponse);
    await awaitDrainingStrayAuthMe(harness.fixture.whenStable());

    // --- 3. Amenities load for the resident's single (auto-selected) building -
    const amenitiesComponent = await awaitDrainingStrayAuthMe(
      harness.navigateByUrl('/amenities', AmenitiesPage)
    );

    httpMock.expectOne('/api/buildings/building-a/amenities').flush([amenity]);
    await awaitDrainingStrayAuthMe(harness.fixture.whenStable());
    harness.fixture.detectChanges();

    expect(harness.routeNativeElement!.textContent).toContain('Pool');

    // --- 4. Select the amenity, quote, and create a Leisure reservation -------
    amenitiesComponent.selectAmenity(amenity);
    harness.fixture.detectChanges();

    const leisureDebugElement = harness.fixture.debugElement.query(
      By.directive(LeisureReservationComponent)
    );
    expect(leisureDebugElement).toBeTruthy();
    const leisureComponent = leisureDebugElement.componentInstance as LeisureReservationComponent;

    leisureComponent.requestQuote();
    httpMock.expectOne((req) => req.url === '/api/pricing/quote').flush(quote);
    await awaitDrainingStrayAuthMe(harness.fixture.whenStable());
    harness.fixture.detectChanges();

    leisureComponent.create();
    httpMock.expectOne('/api/reservations').flush(createdReservation);
    await awaitDrainingStrayAuthMe(harness.fixture.whenStable());
    harness.fixture.detectChanges();

    const afterCreateText = harness.routeNativeElement!.textContent!;
    expect(afterCreateText).toContain('Reservation hold created');
    expect(afterCreateText).toContain('Pending');

    // --- 5. Navigate to the payment page and declare cash ----------------------
    const paymentComponent = await awaitDrainingStrayAuthMe(
      harness.navigateByUrl('/reservations/reservation-1/payment', PaymentPage)
    );

    httpMock.expectOne('/api/reservations/reservation-1').flush(createdReservation);
    await awaitDrainingStrayAuthMe(harness.fixture.whenStable());
    harness.fixture.detectChanges();

    paymentComponent.declareCash();

    httpMock.expectOne('/api/reservations/reservation-1/payments/cash').flush({
      paymentId: 'payment-1',
      status: 'Pending',
      reservationExpiresAtUtc: '2026-10-01T00:30:00Z'
    });

    httpMock.expectOne('/api/payments/payment-1').flush(pendingCashPayment);
    await awaitDrainingStrayAuthMe(harness.fixture.whenStable());
    harness.fixture.detectChanges();

    // --- 6. Cash Pending must never be shown/treated as Reservation Confirmed -
    const pageText = harness.routeNativeElement!.textContent!;
    expect(pageText).toContain('Efectivo declarado');
    expect(pageText).toContain('Pendiente');
    expect(pageText).toContain('todavía NO confirma la reserva');
    expect(pageText).not.toContain('reserva confirmada');
    expect(pageText).not.toContain('Pago aprobado y reserva confirmada');
    expect(paymentComponent.reservation()?.status).toBe('Pending');
  });
});
