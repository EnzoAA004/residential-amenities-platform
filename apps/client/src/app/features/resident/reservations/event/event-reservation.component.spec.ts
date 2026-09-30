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
import { PriceQuote } from '../reservation.models';
import { EventReservationComponent } from './event-reservation.component';

const sumExclusive: AmenitySummary = {
  id: 'sum-1',
  name: 'SUM',
  kind: 'Sum',
  allowsSharedUse: true,
  allowsExclusiveUse: true
};

const sumNotExclusive: AmenitySummary = {
  id: 'sum-2',
  name: 'SUM Shared Only',
  kind: 'Sum',
  allowsSharedUse: true,
  allowsExclusiveUse: false
};

const poolExclusive: AmenitySummary = {
  id: 'pool-1',
  name: 'Pool',
  kind: 'Pool',
  allowsSharedUse: true,
  allowsExclusiveUse: true
};

const barbecueExclusive: AmenitySummary = {
  id: 'barbecue-1',
  name: 'Barbecue',
  kind: 'Barbecue',
  allowsSharedUse: true,
  allowsExclusiveUse: true
};

const secondPoolExclusive: AmenitySummary = {
  id: 'pool-2',
  name: 'Second Pool',
  kind: 'Pool',
  allowsSharedUse: true,
  allowsExclusiveUse: true
};

const poolNotExclusive: AmenitySummary = {
  id: 'pool-3',
  name: 'Shared-only Pool',
  kind: 'Pool',
  allowsSharedUse: true,
  allowsExclusiveUse: false
};

const otherAmenity: AmenitySummary = {
  id: 'other-1',
  name: 'Coworking',
  kind: 'Other',
  allowsSharedUse: true,
  allowsExclusiveUse: true
};

const quote: PriceQuote = {
  currency: 'ARS',
  totalAmount: 15000,
  quotedAtUtc: '2026-01-01T00:00:00Z',
  lines: [{ priceRuleId: 'rule-1', amenityId: 'sum-1', componentType: 'Base', currency: 'ARS', amount: 15000 }]
};

const slot1 = { id: 'slot-1', name: 'Afternoon', startsAtUtc: '2026-10-01T17:00:00Z', endsAtUtc: '2026-10-01T22:00:00Z', isOvernight: false };
const slot2 = { id: 'slot-2', name: 'Evening', startsAtUtc: '2026-10-01T22:00:00Z', endsAtUtc: '2026-10-02T01:00:00Z', isOvernight: false };

describe('EventReservationComponent', () => {
  let fixture: ComponentFixture<EventReservationComponent>;
  let component: EventReservationComponent;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [EventReservationComponent],
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

  function createFixture(baseAmenity: AmenitySummary, amenities: AmenitySummary[]): void {
    fixture = TestBed.createComponent(EventReservationComponent);
    component = fixture.componentInstance;
    component.amenities = amenities;
    component.baseAmenity = baseAmenity;
    fixture.detectChanges();
  }

  function selectDate(date: string): void {
    component.selectedDate.set(date);
    fixture.detectChanges();
  }

  // --- eligibility -----------------------------------------------------------

  it('shows no functional Event flow when the base SUM does not allow exclusive use', () => {
    createFixture(sumNotExclusive, [sumNotExclusive]);

    expect(fixture.nativeElement.textContent).toContain('Este SUM no admite reservas de tipo Event.');
    httpMock.expectNone(() => true);
  });

  it('offers the Event flow when the base SUM allows exclusive use', () => {
    createFixture(sumExclusive, [sumExclusive]);

    expect(fixture.nativeElement.textContent).not.toContain('no admite reservas de tipo Event');
  });

  it('derives add-on candidates from Pool/Barbecue amenities that allow exclusive use, excluding the base and ineligible amenities', () => {
    createFixture(sumExclusive, [
      sumExclusive,
      poolExclusive,
      barbecueExclusive,
      poolNotExclusive,
      otherAmenity
    ]);

    const ids = component.eligibleAddOns().map((addOn) => addOn.id);
    expect(ids).toEqual(['pool-1', 'barbecue-1']);
    expect(ids).not.toContain(sumExclusive.id);
    expect(ids).not.toContain(poolNotExclusive.id);
    expect(ids).not.toContain(otherAmenity.id);
  });

  it('does not impose any numeric maximum on eligible add-ons', () => {
    createFixture(sumExclusive, [sumExclusive, poolExclusive, secondPoolExclusive, barbecueExclusive]);

    expect(component.eligibleAddOns().map((addOn) => addOn.id)).toEqual(['pool-1', 'pool-2', 'barbecue-1']);
  });

  it('prunes a selected add-on that is no longer eligible when the amenities list changes', () => {
    createFixture(sumExclusive, [sumExclusive, poolExclusive]);
    component.toggleAddOn('pool-1');
    expect(component.isAddOnSelected('pool-1')).toBe(true);

    component.amenities = [sumExclusive];
    fixture.detectChanges();

    expect(component.isAddOnSelected('pool-1')).toBe(false);
  });

  // --- date / slot discovery --------------------------------------------------

  it('does not request slots before a date is selected', () => {
    createFixture(sumExclusive, [sumExclusive]);

    expect(component.slotsState()).toEqual({ status: 'idle' });
    httpMock.expectNone(() => true);
  });

  it('requests slots with the literal date string, never a reconstructed Date', async () => {
    createFixture(sumExclusive, [sumExclusive]);

    selectDate('2026-10-01');

    const request = httpMock.expectOne(
      (req) => req.url === '/api/buildings/building-a/event-slots' && req.params.get('date') === '2026-10-01'
    );
    request.flush([slot1, slot2]);
    await fixture.whenStable();

    expect(component.slotsState()).toEqual({ status: 'success', slots: [slot1, slot2] });
  });

  it('shows the empty state for a date with no configured slots', async () => {
    createFixture(sumExclusive, [sumExclusive]);
    selectDate('2026-10-02');

    httpMock.expectOne((req) => req.url === '/api/buildings/building-a/event-slots').flush([]);
    await fixture.whenStable();

    expect(component.slotsState()).toEqual({ status: 'empty' });
  });

  it('shows a 403 without signing out', async () => {
    createFixture(sumExclusive, [sumExclusive]);
    selectDate('2026-10-01');

    httpMock
      .expectOne((req) => req.url === '/api/buildings/building-a/event-slots')
      .flush({ status: 403, title: 'You do not have access to this building.' }, { status: 403, statusText: 'Forbidden' });
    await fixture.whenStable();

    expect(component.slotsState().status).toBe('error');
    expect(TestBed.inject(AuthSessionStore).isAuthenticated()).toBe(true);
  });

  it('shows the real title and detail for a 422 (DST invalid/ambiguous)', async () => {
    createFixture(sumExclusive, [sumExclusive]);
    selectDate('2028-03-12');

    httpMock.expectOne((req) => req.url === '/api/buildings/building-a/event-slots').flush(
      {
        status: 422,
        title: "This Event slot cannot be represented unambiguously on this date in the building's time zone.",
        detail: '2028-03-12 02:15 does not exist in time zone America/New_York.'
      },
      { status: 422, statusText: 'Unprocessable Entity' }
    );
    await fixture.whenStable();
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain(
      "This Event slot cannot be represented unambiguously on this date in the building's time zone."
    );
    expect(fixture.nativeElement.textContent).toContain('does not exist in time zone America/New_York.');
  });

  it('treats a network failure on slot discovery as retryable, not fatal', async () => {
    createFixture(sumExclusive, [sumExclusive]);
    selectDate('2026-10-01');

    httpMock
      .expectOne((req) => req.url === '/api/buildings/building-a/event-slots')
      .error(new ProgressEvent('error'), { status: 0, statusText: 'Unknown Error' });
    await fixture.whenStable();

    expect(component.slotsState().status).toBe('error');

    component.reloadSlots();
    httpMock.expectOne((req) => req.url === '/api/buildings/building-a/event-slots').flush([slot1]);
  });

  it('selecting a slot works only from the slots actually returned by the backend', async () => {
    createFixture(sumExclusive, [sumExclusive]);
    selectDate('2026-10-01');
    httpMock.expectOne((req) => req.url === '/api/buildings/building-a/event-slots').flush([slot1]);
    await fixture.whenStable();

    component.selectSlot(slot1);

    expect(component.selectedSlot()).toEqual(slot1);
  });

  it('clears the selected slot and invalidates the quote when the date changes', async () => {
    createFixture(sumExclusive, [sumExclusive]);
    selectDate('2026-10-01');
    httpMock.expectOne((req) => req.url === '/api/buildings/building-a/event-slots').flush([slot1]);
    await fixture.whenStable();
    component.selectSlot(slot1);

    selectDate('2026-10-02');
    httpMock.expectOne((req) => req.url === '/api/buildings/building-a/event-slots').flush([slot2]);
    await fixture.whenStable();

    expect(component.selectedSlot()).toBeNull();
    expect(component.quoteState()).toEqual({ status: 'idle' });
  });

  it('ignores a stale slots response from an abandoned date (date A -> date B race)', async () => {
    createFixture(sumExclusive, [sumExclusive]);

    selectDate('2026-10-01');
    const staleRequest = httpMock.expectOne((req) => req.url === '/api/buildings/building-a/event-slots');

    selectDate('2026-10-02');
    const freshRequest = httpMock.expectOne((req) => req.url === '/api/buildings/building-a/event-slots');

    expect(() => staleRequest.flush([slot1])).toThrow();

    freshRequest.flush([slot2]);
    await fixture.whenStable();

    expect(component.slotsState()).toEqual({ status: 'success', slots: [slot2] });
  });

  // --- quote ------------------------------------------------------------------

  async function selectSlotAndReachQuoteReady(): Promise<void> {
    createFixture(sumExclusive, [sumExclusive, poolExclusive, barbecueExclusive]);
    selectDate('2026-10-01');
    httpMock.expectOne((req) => req.url === '/api/buildings/building-a/event-slots').flush([slot1]);
    await fixture.whenStable();
    component.selectSlot(slot1);
  }

  it('quotes with no addOnAmenityId when there are no add-ons selected', async () => {
    await selectSlotAndReachQuoteReady();

    component.requestQuote();

    const request = httpMock.expectOne((req) => req.url === '/api/pricing/quote');
    expect(request.request.params.has('addOnAmenityId')).toBe(false);
    expect(request.request.params.get('useType')).toBe('Event');
    request.flush(quote);
  });

  it('quotes with a repeated addOnAmenityId for each selected add-on', async () => {
    await selectSlotAndReachQuoteReady();
    component.toggleAddOn('pool-1');
    component.toggleAddOn('barbecue-1');

    component.requestQuote();

    const request = httpMock.expectOne((req) => req.url === '/api/pricing/quote');
    expect(request.request.params.getAll('addOnAmenityId')).toEqual(['pool-1', 'barbecue-1']);
    request.flush({ ...quote, totalAmount: 21000 });
  });

  it('shows the real title and detail on a quote 422', async () => {
    await selectSlotAndReachQuoteReady();
    component.requestQuote();

    httpMock
      .expectOne((req) => req.url === '/api/pricing/quote')
      .flush(
        { status: 422, title: 'Unable to calculate a price quote.', detail: 'No active AddOn price rule.' },
        { status: 422, statusText: 'Unprocessable Entity' }
      );
    await fixture.whenStable();
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Unable to calculate a price quote.');
    expect(fixture.nativeElement.textContent).toContain('No active AddOn price rule.');
  });

  it('invalidates the quote when the add-on selection changes', async () => {
    await selectSlotAndReachQuoteReady();
    component.requestQuote();
    httpMock.expectOne((req) => req.url === '/api/pricing/quote').flush(quote);
    await fixture.whenStable();
    expect(component.quoteState().status).toBe('success');

    component.toggleAddOn('pool-1');

    expect(component.quoteState()).toEqual({ status: 'idle' });
  });

  it('invalidates the quote when the slot changes', async () => {
    createFixture(sumExclusive, [sumExclusive]);
    selectDate('2026-10-01');
    httpMock.expectOne((req) => req.url === '/api/buildings/building-a/event-slots').flush([slot1, slot2]);
    await fixture.whenStable();
    component.selectSlot(slot1);
    component.requestQuote();
    httpMock.expectOne((req) => req.url === '/api/pricing/quote').flush(quote);
    await fixture.whenStable();

    component.selectSlot(slot2);

    expect(component.quoteState()).toEqual({ status: 'idle' });
  });

  it('ignores a stale quote response after the add-on selection changes', async () => {
    await selectSlotAndReachQuoteReady();
    component.requestQuote();
    const staleRequest = httpMock.expectOne((req) => req.url === '/api/pricing/quote');

    component.toggleAddOn('pool-1');
    component.requestQuote();
    const freshRequest = httpMock.expectOne((req) => req.url === '/api/pricing/quote');

    // Quote uses a contextVersion guard (like #47), not switchMap
    // cancellation — the stale request still resolves, but its handler
    // is a no-op once the context has moved on.
    staleRequest.flush(quote);
    await fixture.whenStable();
    expect(component.quoteState().status).toBe('loading');

    freshRequest.flush({ ...quote, totalAmount: 18000 });
    await fixture.whenStable();

    expect(component.quoteState()).toEqual({
      status: 'success',
      quote: { ...quote, totalAmount: 18000 }
    });
  });

  // --- create -------------------------------------------------------------

  it('sends startsAtUtc/endsAtUtc exactly as returned by the selected slot occurrence', async () => {
    await selectSlotAndReachQuoteReady();
    component.requestQuote();
    httpMock.expectOne((req) => req.url === '/api/pricing/quote').flush(quote);
    await fixture.whenStable();

    component.create();

    const request = httpMock.expectOne('/api/reservations');
    const body = request.request.body as Record<string, unknown>;
    expect(body['startsAtUtc']).toBe(slot1.startsAtUtc);
    expect(body['endsAtUtc']).toBe(slot1.endsAtUtc);
    expect(body['useType']).toBe('Event');
    expect(body['addOnAmenityIds']).toEqual([]);
    request.flush({
      id: 'reservation-1',
      buildingId: 'building-a',
      useType: 'Event',
      status: 'Pending',
      startsAtUtc: slot1.startsAtUtc,
      endsAtUtc: slot1.endsAtUtc,
      createdAtUtc: '2026-01-01T00:00:00Z',
      expiresAtUtc: '2026-01-01T00:30:00Z',
      resources: [{ amenityId: 'sum-1', isExclusive: true }],
      priceLines: [{ amenityId: 'sum-1', componentType: 'Base', currency: 'ARS', amount: 15000 }],
      currency: 'ARS',
      totalAmount: 15000
    });
  });

  it('uses selected add-on ids exactly once each in the create body', async () => {
    await selectSlotAndReachQuoteReady();
    component.toggleAddOn('pool-1');
    component.toggleAddOn('barbecue-1');
    component.requestQuote();
    httpMock.expectOne((req) => req.url === '/api/pricing/quote').flush({ ...quote, totalAmount: 21000 });
    await fixture.whenStable();

    component.create();

    const request = httpMock.expectOne('/api/reservations');
    expect(request.request.body.addOnAmenityIds).toEqual(['pool-1', 'barbecue-1']);
    request.flush({
      id: 'reservation-1',
      buildingId: 'building-a',
      useType: 'Event',
      status: 'Pending',
      startsAtUtc: slot1.startsAtUtc,
      endsAtUtc: slot1.endsAtUtc,
      createdAtUtc: '2026-01-01T00:00:00Z',
      expiresAtUtc: '2026-01-01T00:30:00Z',
      resources: [],
      priceLines: [],
      currency: 'ARS',
      totalAmount: 21000
    });
  });

  it('shows the real 201 snapshot and flags a price change, without rollback', async () => {
    await selectSlotAndReachQuoteReady();
    component.requestQuote();
    httpMock.expectOne((req) => req.url === '/api/pricing/quote').flush(quote); // 15000
    await fixture.whenStable();

    component.create();
    httpMock.expectOne('/api/reservations').flush({
      id: 'reservation-1',
      buildingId: 'building-a',
      useType: 'Event',
      status: 'Pending',
      startsAtUtc: slot1.startsAtUtc,
      endsAtUtc: slot1.endsAtUtc,
      createdAtUtc: '2026-01-01T00:00:00Z',
      expiresAtUtc: '2026-01-01T00:30:00Z',
      resources: [{ amenityId: 'sum-1', isExclusive: true }],
      priceLines: [{ amenityId: 'sum-1', componentType: 'Base', currency: 'ARS', amount: 16500 }],
      currency: 'ARS',
      totalAmount: 16500
    });
    await fixture.whenStable();
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain(component.formatAmount(16500, 'ARS'));
    expect(fixture.nativeElement.textContent).toContain(
      'El precio se actualizó al crear la reserva. Este es el importe registrado en el hold.'
    );
  });

  it('shows a clear 409 conflict message', async () => {
    await selectSlotAndReachQuoteReady();
    component.requestQuote();
    httpMock.expectOne((req) => req.url === '/api/pricing/quote').flush(quote);
    await fixture.whenStable();

    component.create();
    httpMock
      .expectOne('/api/reservations')
      .flush({ status: 409, title: 'This time range is not available.' }, { status: 409, statusText: 'Conflict' });
    await fixture.whenStable();

    expect(component.createState()).toEqual({
      status: 'error',
      error: { status: 409, title: 'This time range is not available.' }
    });
  });

  it('shows the real 422 detail for an out-of-slot range without inventing a "correct" schedule', async () => {
    await selectSlotAndReachQuoteReady();
    component.requestQuote();
    httpMock.expectOne((req) => req.url === '/api/pricing/quote').flush(quote);
    await fixture.whenStable();

    component.create();
    httpMock.expectOne('/api/reservations').flush(
      { status: 422, title: 'Unable to create this reservation.', detail: 'No matching Event slot.' },
      { status: 422, statusText: 'Unprocessable Entity' }
    );
    await fixture.whenStable();
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Unable to create this reservation.');
    expect(fixture.nativeElement.textContent).toContain('No matching Event slot.');
  });

  it('shows a 403 on create without signing out', async () => {
    await selectSlotAndReachQuoteReady();
    component.requestQuote();
    httpMock.expectOne((req) => req.url === '/api/pricing/quote').flush(quote);
    await fixture.whenStable();

    component.create();
    httpMock
      .expectOne('/api/reservations')
      .flush({ status: 403, title: 'Forbidden.' }, { status: 403, statusText: 'Forbidden' });
    await fixture.whenStable();

    expect(component.createState().status).toBe('error');
    expect(TestBed.inject(AuthSessionStore).isAuthenticated()).toBe(true);
  });

  it('treats a network failure during create as uncertain and blocks a second POST', async () => {
    await selectSlotAndReachQuoteReady();
    component.requestQuote();
    httpMock.expectOne((req) => req.url === '/api/pricing/quote').flush(quote);
    await fixture.whenStable();

    component.create();
    httpMock.expectOne('/api/reservations').error(new ProgressEvent('error'), { status: 0, statusText: 'Unknown Error' });
    await fixture.whenStable();

    expect(component.createState()).toEqual({ status: 'unknown' });

    component.create();
    httpMock.expectNone('/api/reservations');
  });

  it('does not send a second POST for a rapid double click', async () => {
    await selectSlotAndReachQuoteReady();
    component.requestQuote();
    httpMock.expectOne((req) => req.url === '/api/pricing/quote').flush(quote);
    await fixture.whenStable();

    component.create();
    component.create();

    httpMock.expectOne('/api/reservations').flush({
      id: 'reservation-1',
      buildingId: 'building-a',
      useType: 'Event',
      status: 'Pending',
      startsAtUtc: slot1.startsAtUtc,
      endsAtUtc: slot1.endsAtUtc,
      createdAtUtc: '2026-01-01T00:00:00Z',
      expiresAtUtc: '2026-01-01T00:30:00Z',
      resources: [],
      priceLines: [],
      currency: 'ARS',
      totalAmount: 15000
    });
  });

  it(
    'never shows a late 201 for an abandoned SUM/date/slot/add-on context as a confirmation of the new one',
    async () => {
      await selectSlotAndReachQuoteReady();
      component.requestQuote();
      httpMock.expectOne((req) => req.url === '/api/pricing/quote').flush(quote);
      await fixture.whenStable();

      component.create();
      const staleCreateRequest = httpMock.expectOne('/api/reservations');

      // The resident abandons this context by picking a different amenity.
      component.baseAmenity = { ...sumExclusive, id: 'sum-other' };

      expect(component.createState()).toEqual({ status: 'idle' });

      staleCreateRequest.flush({
        id: 'reservation-1',
        buildingId: 'building-a',
        useType: 'Event',
        status: 'Pending',
        startsAtUtc: slot1.startsAtUtc,
        endsAtUtc: slot1.endsAtUtc,
        createdAtUtc: '2026-01-01T00:00:00Z',
        expiresAtUtc: '2026-01-01T00:30:00Z',
        resources: [],
        priceLines: [],
        currency: 'ARS',
        totalAmount: 15000
      });
      await fixture.whenStable();
      fixture.detectChanges();

      expect(component.createState()).toEqual({ status: 'idle' });
      expect(fixture.nativeElement.textContent).not.toContain('Event reservation hold created');
    }
  );

  it('offers a link to continue to payment for the created reservation', async () => {
    await selectSlotAndReachQuoteReady();
    component.requestQuote();
    httpMock.expectOne((req) => req.url === '/api/pricing/quote').flush(quote);
    await fixture.whenStable();

    component.create();
    httpMock.expectOne('/api/reservations').flush({
      id: 'reservation-1',
      buildingId: 'building-a',
      useType: 'Event',
      status: 'Pending',
      startsAtUtc: slot1.startsAtUtc,
      endsAtUtc: slot1.endsAtUtc,
      createdAtUtc: '2026-01-01T00:00:00Z',
      expiresAtUtc: '2026-01-01T00:30:00Z',
      resources: [],
      priceLines: [],
      currency: 'ARS',
      totalAmount: 15000
    });
    await fixture.whenStable();
    fixture.detectChanges();

    const linkDebugElement = fixture.debugElement.query(By.directive(RouterLink));
    expect(linkDebugElement).toBeTruthy();
    // `routerLink` is a write-only setter in this Angular version (no
    // getter) — the resolved `urlTree` is the only readable target.
    expect(linkDebugElement.injector.get(RouterLink).urlTree?.toString()).toBe(
      '/reservations/reservation-1/payment'
    );
  });

  it('resets the whole flow only via the explicit "create another" action', async () => {
    await selectSlotAndReachQuoteReady();
    component.requestQuote();
    httpMock.expectOne((req) => req.url === '/api/pricing/quote').flush(quote);
    await fixture.whenStable();

    component.create();
    httpMock.expectOne('/api/reservations').flush({
      id: 'reservation-1',
      buildingId: 'building-a',
      useType: 'Event',
      status: 'Pending',
      startsAtUtc: slot1.startsAtUtc,
      endsAtUtc: slot1.endsAtUtc,
      createdAtUtc: '2026-01-01T00:00:00Z',
      expiresAtUtc: '2026-01-01T00:30:00Z',
      resources: [],
      priceLines: [],
      currency: 'ARS',
      totalAmount: 15000
    });
    await fixture.whenStable();
    expect(component.createState().status).toBe('created');

    component.resetFlow();

    expect(component.createState()).toEqual({ status: 'idle' });
    expect(component.quoteState()).toEqual({ status: 'idle' });
    expect(component.selectedSlot()).toBeNull();
    expect(component.selectedDate()).toBe('');
  });
});
