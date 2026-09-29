import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { RouterLink, provideRouter } from '@angular/router';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';

import { API_BASE_URL } from '../../../../core/config/api-base-url.token';
import { AuthSessionStore } from '../../../../core/auth/auth-session.store';
import { ResidentContextStore } from '../../../../core/resident-context/resident-context.store';
import { AmenitySummary } from '../../amenities/amenities.models';
import { LeisureReservationComponent } from './leisure-reservation.component';
import { PriceQuote, Reservation } from '../reservation.models';

const sharedOnlyAmenity: AmenitySummary = {
  id: 'amenity-shared',
  name: 'Pool',
  kind: 'Pool',
  allowsSharedUse: true,
  allowsExclusiveUse: false
};

const exclusiveOnlyAmenity: AmenitySummary = {
  id: 'amenity-exclusive',
  name: 'SUM',
  kind: 'Sum',
  allowsSharedUse: false,
  allowsExclusiveUse: true
};

const bothAmenity: AmenitySummary = {
  id: 'amenity-both',
  name: 'Gym',
  kind: 'Fitness',
  allowsSharedUse: true,
  allowsExclusiveUse: true
};

const neitherAmenity: AmenitySummary = {
  id: 'amenity-neither',
  name: 'Storage',
  kind: 'Other',
  allowsSharedUse: false,
  allowsExclusiveUse: false
};

const quote: PriceQuote = {
  currency: 'ARS',
  totalAmount: 5000,
  quotedAtUtc: '2026-01-01T00:00:00Z',
  lines: [{ priceRuleId: 'rule-1', amenityId: 'amenity-both', componentType: 'Base', currency: 'ARS', amount: 5000 }]
};

function reservationWith(overrides: Partial<Reservation> = {}): Reservation {
  return {
    id: 'reservation-1',
    buildingId: 'building-a',
    useType: 'SharedLeisure',
    status: 'Pending',
    startsAtUtc: '2026-01-05T10:00:00Z',
    endsAtUtc: '2026-01-05T12:00:00Z',
    createdAtUtc: '2026-01-01T00:00:00Z',
    expiresAtUtc: '2026-01-01T00:30:00Z',
    confirmedAtUtc: null,
    cancelledAtUtc: null,
    expiredAtUtc: null,
    cancellationReason: null,
    resources: [{ amenityId: 'amenity-both', isExclusive: false }],
    priceLines: [{ amenityId: 'amenity-both', componentType: 'Base', currency: 'ARS', amount: 5000 }],
    currency: 'ARS',
    totalAmount: 5000,
    ...overrides
  };
}

describe('LeisureReservationComponent', () => {
  let fixture: ComponentFixture<LeisureReservationComponent>;
  let component: LeisureReservationComponent;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [LeisureReservationComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: API_BASE_URL, useValue: '/api' },
        AuthSessionStore,
        ResidentContextStore
      ]
    });

    const session = TestBed.inject(AuthSessionStore);
    session.setAuthenticated({
      id: 'user-1',
      email: 'resident@example.test',
      displayName: 'Resident',
      roles: ['Resident'],
      memberships: [{ buildingId: 'building-a', unitId: 'unit-a', unit: '1A', building: 'Building A' }]
    });

    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  function createFixture(amenity: AmenitySummary): void {
    fixture = TestBed.createComponent(LeisureReservationComponent);
    component = fixture.componentInstance;
    component.amenity = amenity;
    fixture.detectChanges();
  }

  it('auto-selects SharedLeisure when the amenity only allows shared use', () => {
    createFixture(sharedOnlyAmenity);

    expect(component.eligibleUseTypes()).toEqual(['SharedLeisure']);
    expect(component.form.controls.useType.value).toBe('SharedLeisure');
  });

  it('auto-selects ExclusiveLeisure when the amenity only allows exclusive use', () => {
    createFixture(exclusiveOnlyAmenity);

    expect(component.eligibleUseTypes()).toEqual(['ExclusiveLeisure']);
    expect(component.form.controls.useType.value).toBe('ExclusiveLeisure');
  });

  it('requires an explicit choice when both use types are allowed', () => {
    createFixture(bothAmenity);

    expect(component.eligibleUseTypes()).toEqual(['SharedLeisure', 'ExclusiveLeisure']);
    expect(component.form.controls.useType.value).toBeNull();
  });

  it('shows no functional reservation form when the amenity allows neither use type', () => {
    createFixture(neitherAmenity);

    expect(component.eligibleUseTypes()).toEqual([]);
    expect(fixture.nativeElement.textContent).toContain('Esta amenity no admite reservas de ocio.');
    httpMock.expectNone(() => true);
  });

  it('does not request a quote without an explicit use type chosen', () => {
    createFixture(bothAmenity);

    component.requestQuote();

    expect(component.useTypeError()).toBeTruthy();
    httpMock.expectNone(() => true);
  });

  it('does not request a quote or create for an invalid range', () => {
    createFixture(sharedOnlyAmenity);
    component.form.controls.from.setValue('2026-01-10T10:00');
    component.form.controls.to.setValue('2026-01-01T10:00');

    component.requestQuote();

    expect(component.rangeError()).toBeTruthy();
    httpMock.expectNone(() => true);
  });

  it('does not request a quote for a range longer than 62 days', () => {
    createFixture(sharedOnlyAmenity);
    component.form.controls.from.setValue('2026-01-01T00:00');
    component.form.controls.to.setValue('2026-04-15T00:00');

    component.requestQuote();

    expect(component.rangeError()).toContain('62');
    httpMock.expectNone(() => true);
  });

  it('requests a quote with buildingId from ResidentContextStore, not from any editable field', () => {
    createFixture(sharedOnlyAmenity);

    component.requestQuote();

    const request = httpMock.expectOne(
      (req) => req.url === '/api/pricing/quote' && req.params.get('buildingId') === 'building-a'
    );
    request.flush(quote);
  });

  it('shows currency and totalAmount on a successful quote', async () => {
    createFixture(sharedOnlyAmenity);
    component.requestQuote();

    httpMock.expectOne((req) => req.url === '/api/pricing/quote').flush(quote);
    await fixture.whenStable();
    fixture.detectChanges();

    expect(component.quoteState()).toEqual({ status: 'success', quote });
    expect(fixture.nativeElement.textContent).toContain(component.formatAmount(5000, 'ARS'));
  });

  it('shows the real title and detail when the quote fails with 422 (no active price rule)', async () => {
    createFixture(sharedOnlyAmenity);
    component.requestQuote();

    httpMock
      .expectOne((req) => req.url === '/api/pricing/quote')
      .flush(
        {
          status: 422,
          title: 'Unable to calculate a price quote.',
          detail: 'No active Base price rule.'
        },
        { status: 422, statusText: 'Unprocessable Entity' }
      );
    await fixture.whenStable();
    fixture.detectChanges();

    expect(component.quoteState()).toEqual({
      status: 'error',
      error: {
        status: 422,
        title: 'Unable to calculate a price quote.',
        detail: 'No active Base price rule.'
      }
    });
    expect(fixture.nativeElement.textContent).toContain('Unable to calculate a price quote.');
    expect(fixture.nativeElement.textContent).toContain('No active Base price rule.');
  });

  it('shows a safe error message when the quote fails with 403 (invalid context)', async () => {
    createFixture(sharedOnlyAmenity);
    component.requestQuote();

    httpMock
      .expectOne((req) => req.url === '/api/pricing/quote')
      .flush(
        { status: 403, title: 'You do not have access to this building.' },
        { status: 403, statusText: 'Forbidden' }
      );
    await fixture.whenStable();

    expect(component.quoteState().status).toBe('error');
  });

  it('invalidates the previous quote when the use type changes', async () => {
    createFixture(bothAmenity);
    component.selectUseType('SharedLeisure');
    component.requestQuote();
    httpMock.expectOne((req) => req.url === '/api/pricing/quote').flush(quote);
    await fixture.whenStable();
    expect(component.quoteState().status).toBe('success');

    component.selectUseType('ExclusiveLeisure');

    expect(component.quoteState()).toEqual({ status: 'idle' });
  });

  it('discards a stale quote for a use type the resident already changed away from', async () => {
    createFixture(bothAmenity);
    component.selectUseType('SharedLeisure');
    component.requestQuote();
    const sharedRequest = httpMock.expectOne((req) => req.url === '/api/pricing/quote');

    component.selectUseType('ExclusiveLeisure');
    expect(component.quoteState()).toEqual({ status: 'idle' });

    // The late Shared quote response must never resurrect as a usable
    // quote for the now-selected Exclusive use type.
    sharedRequest.flush(quote);
    await fixture.whenStable();

    expect(component.quoteState()).toEqual({ status: 'idle' });

    // And "Confirm reservation" must not be usable off that stale quote.
    component.create();
    httpMock.expectNone((req) => req.url === '/api/reservations');
  });

  it('discards a stale quote for an amenity the resident has already switched away from', async () => {
    createFixture(sharedOnlyAmenity);
    component.requestQuote();
    const staleRequest = httpMock.expectOne((req) => req.url === '/api/pricing/quote');

    // The resident picks a different amenity while amenity A's quote is
    // still in flight — LeisureReservationComponent is not destroyed here,
    // Angular just rebinds the [amenity] input on the same instance.
    component.amenity = exclusiveOnlyAmenity;
    expect(component.quoteState()).toEqual({ status: 'idle' });

    staleRequest.flush(quote);
    await fixture.whenStable();

    expect(component.quoteState()).toEqual({ status: 'idle' });
  });

  it('does not allow create without a successful quote first', () => {
    createFixture(sharedOnlyAmenity);

    component.create();

    httpMock.expectNone((req) => req.url === '/api/reservations');
  });

  it('sends exactly buildingId/amenityId/useType/startsAtUtc/endsAtUtc on create, and shows the 201 hold', async () => {
    createFixture(sharedOnlyAmenity);
    component.requestQuote();
    httpMock.expectOne((req) => req.url === '/api/pricing/quote').flush(quote);
    await fixture.whenStable();

    component.create();

    const request = httpMock.expectOne('/api/reservations');
    const body = request.request.body as Record<string, unknown>;
    expect(Object.keys(body).sort()).toEqual(
      ['amenityId', 'buildingId', 'endsAtUtc', 'startsAtUtc', 'useType'].sort()
    );
    expect(body['buildingId']).toBe('building-a');
    expect(body['amenityId']).toBe('amenity-shared');
    expect(body['useType']).toBe('SharedLeisure');

    const reservation = reservationWith({ totalAmount: 5000, currency: 'ARS' });
    request.flush(reservation);
    await fixture.whenStable();
    fixture.detectChanges();

    expect(component.createState()).toEqual({ status: 'created', reservation, quotedAt: quote });
    expect(fixture.nativeElement.textContent).toContain('Pending');
  });

  it('shows the real 201 price snapshot and flags when it differs from the quote, without rollback', async () => {
    createFixture(sharedOnlyAmenity);
    component.requestQuote();
    httpMock.expectOne((req) => req.url === '/api/pricing/quote').flush(quote); // totalAmount 5000
    await fixture.whenStable();

    component.create();
    const reservation = reservationWith({ totalAmount: 6500 });
    httpMock.expectOne('/api/reservations').flush(reservation);
    await fixture.whenStable();
    fixture.detectChanges();

    const state = component.createState();
    expect(state).toEqual({ status: 'created', reservation, quotedAt: quote });
    expect(fixture.nativeElement.textContent).toContain(component.formatAmount(6500, 'ARS'));
    expect(fixture.nativeElement.textContent).toContain(
      'El precio se actualizó al crear la reserva. Este es el importe registrado en el hold.'
    );
  });

  it('offers a link to continue to payment for the created reservation, not a query-string price', async () => {
    createFixture(sharedOnlyAmenity);
    component.requestQuote();
    httpMock.expectOne((req) => req.url === '/api/pricing/quote').flush(quote);
    await fixture.whenStable();

    component.create();
    const reservation = reservationWith();
    httpMock.expectOne('/api/reservations').flush(reservation);
    await fixture.whenStable();
    fixture.detectChanges();

    const linkDebugElement = fixture.debugElement.query(By.directive(RouterLink));
    expect(linkDebugElement).toBeTruthy();
    // `routerLink` is a write-only setter in this Angular version (no
    // getter) — the resolved `urlTree` is the only readable target.
    expect(linkDebugElement.injector.get(RouterLink).urlTree?.toString()).toBe(
      `/reservations/${reservation.id}/payment`
    );
  });

  it('shows a clear 409 conflict message and leaves no phantom reservation state', async () => {
    createFixture(sharedOnlyAmenity);
    component.requestQuote();
    httpMock.expectOne((req) => req.url === '/api/pricing/quote').flush(quote);
    await fixture.whenStable();

    component.create();
    httpMock.expectOne('/api/reservations').flush(
      { status: 409, title: 'This time range is not available.' },
      { status: 409, statusText: 'Conflict' }
    );
    await fixture.whenStable();

    expect(component.createState()).toEqual({
      status: 'error',
      error: { status: 409, title: 'This time range is not available.' }
    });
  });

  it('shows a clear 422 business-validation message on create', async () => {
    createFixture(sharedOnlyAmenity);
    component.requestQuote();
    httpMock.expectOne((req) => req.url === '/api/pricing/quote').flush(quote);
    await fixture.whenStable();

    component.create();
    httpMock.expectOne('/api/reservations').flush(
      { status: 422, title: 'Unable to create this reservation.', detail: 'Outside availability.' },
      { status: 422, statusText: 'Unprocessable Entity' }
    );
    await fixture.whenStable();
    fixture.detectChanges();

    expect(component.createState().status).toBe('error');
    expect(fixture.nativeElement.textContent).toContain('Unable to create this reservation.');
    expect(fixture.nativeElement.textContent).toContain('Outside availability.');
  });

  it('shows a clear 400 invalid-range message on create', async () => {
    createFixture(sharedOnlyAmenity);
    component.requestQuote();
    httpMock.expectOne((req) => req.url === '/api/pricing/quote').flush(quote);
    await fixture.whenStable();

    component.create();
    httpMock.expectOne('/api/reservations').flush(
      { status: 400, title: 'Unable to create this reservation.', detail: 'End must be after start.' },
      { status: 400, statusText: 'Bad Request' }
    );
    await fixture.whenStable();
    fixture.detectChanges();

    expect(component.createState().status).toBe('error');
    expect(fixture.nativeElement.textContent).toContain('Unable to create this reservation.');
    expect(fixture.nativeElement.textContent).toContain('End must be after start.');
  });

  it('shows a clear 403 message on create without signing the user out', async () => {
    createFixture(sharedOnlyAmenity);
    component.requestQuote();
    httpMock.expectOne((req) => req.url === '/api/pricing/quote').flush(quote);
    await fixture.whenStable();

    const session = TestBed.inject(AuthSessionStore);
    component.create();
    httpMock.expectOne('/api/reservations').flush(
      { status: 403, title: 'An active resident membership for this building is required.' },
      { status: 403, statusText: 'Forbidden' }
    );
    await fixture.whenStable();

    expect(component.createState().status).toBe('error');
    expect(session.isAuthenticated()).toBe(true);
  });

  it('treats a network failure during create as an uncertain result, never a confirmed non-creation', async () => {
    createFixture(sharedOnlyAmenity);
    component.requestQuote();
    httpMock.expectOne((req) => req.url === '/api/pricing/quote').flush(quote);
    await fixture.whenStable();

    component.create();
    const request = httpMock.expectOne('/api/reservations');
    request.error(new ProgressEvent('error'), { status: 0, statusText: 'Unknown Error' });
    await fixture.whenStable();
    fixture.detectChanges();

    expect(component.createState()).toEqual({ status: 'unknown' });
    expect(fixture.nativeElement.textContent).toContain(
      'No pudimos confirmar el resultado de la creación. Evitá repetir inmediatamente la operación.'
    );
  });

  it(
    'blocks a second POST after an uncertain (network) result: quote 200 → create → status 0 → ' +
      'unknown → create() again sends nothing, and no enabled confirm button remains',
    async () => {
      createFixture(sharedOnlyAmenity);
      component.requestQuote();
      httpMock.expectOne((req) => req.url === '/api/pricing/quote').flush(quote);
      await fixture.whenStable();

      component.create();
      const firstRequest = httpMock.expectOne('/api/reservations');
      firstRequest.error(new ProgressEvent('error'), { status: 0, statusText: 'Unknown Error' });
      await fixture.whenStable();
      fixture.detectChanges();

      expect(component.createState()).toEqual({ status: 'unknown' });

      // A second, explicit attempt to create must not fire another POST —
      // this is the behavior, not just the internal state, that matters:
      // the button in the DOM must actually be disabled, not just the
      // guard in create() (defense at both levels).
      const buttons = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('ion-button'));
      const confirmButton = buttons.find((button) => button.textContent?.includes('Confirm reservation'));
      expect(confirmButton?.disabled).toBe(true);

      component.create();

      httpMock.expectNone('/api/reservations');
      expect(component.createState()).toEqual({ status: 'unknown' });
    }
  );

  it('disables further submits while creating and never sends a second POST for one click storm', async () => {
    createFixture(sharedOnlyAmenity);
    component.requestQuote();
    httpMock.expectOne((req) => req.url === '/api/pricing/quote').flush(quote);
    await fixture.whenStable();

    component.create();
    expect(component.createState().status).toBe('creating');

    // A second call while still creating must not fire a second request.
    component.create();

    const request = httpMock.expectOne('/api/reservations');
    request.flush(reservationWith());
    await fixture.whenStable();

    httpMock.verify();
  });

  it('resets the whole flow only through the explicit "create another" action, not automatically', async () => {
    createFixture(sharedOnlyAmenity);
    component.requestQuote();
    httpMock.expectOne((req) => req.url === '/api/pricing/quote').flush(quote);
    await fixture.whenStable();

    component.create();
    httpMock.expectOne('/api/reservations').flush(reservationWith());
    await fixture.whenStable();
    expect(component.createState().status).toBe('created');

    component.resetFlow();

    expect(component.createState()).toEqual({ status: 'idle' });
    expect(component.quoteState()).toEqual({ status: 'idle' });
  });

  it('clears quote/creation state when the amenity changes', async () => {
    createFixture(sharedOnlyAmenity);
    component.requestQuote();
    httpMock.expectOne((req) => req.url === '/api/pricing/quote').flush(quote);
    await fixture.whenStable();
    expect(component.quoteState().status).toBe('success');

    component.amenity = exclusiveOnlyAmenity;

    expect(component.quoteState()).toEqual({ status: 'idle' });
    expect(component.createState()).toEqual({ status: 'idle' });
  });

  it(
    'never shows a late 201 for amenity A as a confirmed reservation once the resident has ' +
      'switched to amenity B (the component is reused, not destroyed)',
    async () => {
      createFixture(sharedOnlyAmenity);
      component.requestQuote();
      httpMock.expectOne((req) => req.url === '/api/pricing/quote').flush(quote);
      await fixture.whenStable();

      component.create();
      const staleCreateRequest = httpMock.expectOne('/api/reservations');
      expect(component.createState().status).toBe('creating');

      // The resident switches amenity while amenity A's POST is still in
      // flight. The setter resets to idle; a subsequent, unrelated request
      // for B is not made automatically — create stays an explicit action.
      component.amenity = exclusiveOnlyAmenity;
      expect(component.createState()).toEqual({ status: 'idle' });
      httpMock.expectNone((req) => req.url === '/api/reservations');

      // Amenity A's 201 finally arrives. It must never surface as a
      // confirmed hold for B — the client cannot undo A's creation
      // server-side, but it must not misattribute it to B either.
      staleCreateRequest.flush(reservationWith({ buildingId: 'building-a', totalAmount: 5000 }));
      await fixture.whenStable();
      fixture.detectChanges();

      expect(component.createState()).toEqual({ status: 'idle' });
      expect(fixture.nativeElement.textContent).not.toContain('Reservation hold created');
    }
  );

  it('never sends addOnAmenityId/membershipId query params on quote', () => {
    createFixture(sharedOnlyAmenity);
    component.requestQuote();

    const request = httpMock.expectOne((req) => req.url === '/api/pricing/quote');
    expect(request.request.params.has('addOnAmenityId')).toBe(false);
    expect(request.request.params.has('membershipId')).toBe(false);
    request.flush(quote);
  });
});
