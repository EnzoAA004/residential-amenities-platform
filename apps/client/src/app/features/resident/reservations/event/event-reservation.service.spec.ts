import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';

import { API_BASE_URL } from '../../../../core/config/api-base-url.token';
import { PriceQuote, Reservation } from '../reservation.models';
import { EventReservationService } from './event-reservation.service';
import { EventSlotOccurrence } from './event-reservation.models';

describe('EventReservationService', () => {
  let service: EventReservationService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        EventReservationService,
        { provide: API_BASE_URL, useValue: '/api' }
      ]
    });

    service = TestBed.inject(EventReservationService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('lists event slots at the building-scoped resident URL with the literal date string', async () => {
    const promise = firstValueFrom(service.listSlots('building-1', '2026-10-01'));

    const request = httpMock.expectOne(
      (req) => req.url === '/api/buildings/building-1/event-slots' && req.params.get('date') === '2026-10-01'
    );
    expect(request.request.method).toBe('GET');

    const slots: EventSlotOccurrence[] = [
      { id: 'slot-1', name: 'Afternoon', startsAtUtc: '2026-10-01T17:00:00Z', endsAtUtc: '2026-10-01T22:00:00Z' }
    ];
    request.flush(slots);

    await expect(promise).resolves.toEqual(slots);
  });

  it('requests a quote with buildingId/amenityId/useType=Event and no addOnAmenityId when there are no add-ons', async () => {
    const promise = firstValueFrom(service.getQuote('building-1', 'sum-1', []));

    const request = httpMock.expectOne(
      (req) =>
        req.url === '/api/pricing/quote' &&
        req.params.get('buildingId') === 'building-1' &&
        req.params.get('amenityId') === 'sum-1' &&
        req.params.get('useType') === 'Event'
    );
    expect(request.request.params.has('addOnAmenityId')).toBe(false);

    const quote: PriceQuote = { currency: 'ARS', totalAmount: 15000, quotedAtUtc: '2026-01-01T00:00:00Z', lines: [] };
    request.flush(quote);

    await expect(promise).resolves.toEqual(quote);
  });

  it('repeats addOnAmenityId once per selected add-on, never as CSV', () => {
    service.getQuote('building-1', 'sum-1', ['pool-1', 'barbecue-1']).subscribe();

    const request = httpMock.expectOne(
      (req) => req.url === '/api/pricing/quote' && req.params.get('useType') === 'Event'
    );
    expect(request.request.params.getAll('addOnAmenityId')).toEqual(['pool-1', 'barbecue-1']);
    request.flush({ currency: 'ARS', totalAmount: 21000, quotedAtUtc: '2026-01-01T00:00:00Z', lines: [] });
  });

  it('sends exactly the Event create fields, with startsAtUtc/endsAtUtc passed through unchanged', async () => {
    const promise = firstValueFrom(
      service.create({
        buildingId: 'building-1',
        amenityId: 'sum-1',
        addOnAmenityIds: ['pool-1'],
        useType: 'Event',
        startsAtUtc: '2026-10-01T17:00:00Z',
        endsAtUtc: '2026-10-01T22:00:00Z'
      })
    );

    const request = httpMock.expectOne('/api/reservations');
    expect(request.request.method).toBe('POST');

    const body = request.request.body as Record<string, unknown>;
    expect(Object.keys(body).sort()).toEqual(
      ['addOnAmenityIds', 'amenityId', 'buildingId', 'endsAtUtc', 'startsAtUtc', 'useType'].sort()
    );
    expect(body['startsAtUtc']).toBe('2026-10-01T17:00:00Z');
    expect(body['endsAtUtc']).toBe('2026-10-01T22:00:00Z');
    expect(body).not.toHaveProperty('membershipId');
    expect(body).not.toHaveProperty('slotId');
    expect(body).not.toHaveProperty('totalAmount');
    expect(body).not.toHaveProperty('status');

    const reservation: Reservation = {
      id: 'reservation-1',
      buildingId: 'building-1',
      useType: 'Event',
      status: 'Pending',
      startsAtUtc: '2026-10-01T17:00:00Z',
      endsAtUtc: '2026-10-01T22:00:00Z',
      createdAtUtc: '2026-01-01T00:00:00Z',
      expiresAtUtc: '2026-01-01T00:30:00Z',
      confirmedAtUtc: null,
      cancelledAtUtc: null,
      expiredAtUtc: null,
      cancellationReason: null,
      resources: [
        { amenityId: 'sum-1', isExclusive: true },
        { amenityId: 'pool-1', isExclusive: true }
      ],
      priceLines: [
        { amenityId: 'sum-1', componentType: 'Base', currency: 'ARS', amount: 15000 },
        { amenityId: 'pool-1', componentType: 'AddOn', currency: 'ARS', amount: 3000 }
      ],
      currency: 'ARS',
      totalAmount: 18000
    };
    request.flush(reservation);

    await expect(promise).resolves.toEqual(reservation);
  });
});
