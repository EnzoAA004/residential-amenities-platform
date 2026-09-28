import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';

import { API_BASE_URL } from '../../../../core/config/api-base-url.token';
import { LeisureReservationService } from './leisure-reservation.service';
import { PriceQuote, Reservation } from './leisure-reservation.models';

describe('LeisureReservationService', () => {
  let service: LeisureReservationService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        LeisureReservationService,
        { provide: API_BASE_URL, useValue: '/api' }
      ]
    });

    service = TestBed.inject(LeisureReservationService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('requests a quote with exactly buildingId, amenityId and useType — no /api/api, no add-ons, no membership', async () => {
    const promise = firstValueFrom(
      service.getQuote('building-1', 'amenity-1', 'SharedLeisure')
    );

    const request = httpMock.expectOne(
      (req) =>
        req.url === '/api/pricing/quote' &&
        req.params.get('buildingId') === 'building-1' &&
        req.params.get('amenityId') === 'amenity-1' &&
        req.params.get('useType') === 'SharedLeisure'
    );
    expect(request.request.method).toBe('GET');
    expect(request.request.params.has('addOnAmenityId')).toBe(false);
    expect(request.request.params.has('membershipId')).toBe(false);
    expect(request.request.params.has('atUtc')).toBe(false);

    const quote: PriceQuote = {
      currency: 'ARS',
      totalAmount: 5000,
      quotedAtUtc: '2026-01-01T00:00:00Z',
      lines: []
    };
    request.flush(quote);

    await expect(promise).resolves.toEqual(quote);
  });

  it('sends exactly the Leisure create fields and nothing else', async () => {
    const promise = firstValueFrom(
      service.create({
        buildingId: 'building-1',
        amenityId: 'amenity-1',
        useType: 'ExclusiveLeisure',
        startsAtUtc: '2026-01-05T10:00:00.000Z',
        endsAtUtc: '2026-01-05T12:00:00.000Z'
      })
    );

    const request = httpMock.expectOne('/api/reservations');
    expect(request.request.method).toBe('POST');

    const body = request.request.body as Record<string, unknown>;
    expect(Object.keys(body).sort()).toEqual(
      ['amenityId', 'buildingId', 'endsAtUtc', 'startsAtUtc', 'useType'].sort()
    );
    expect(body).not.toHaveProperty('membershipId');
    expect(body).not.toHaveProperty('userId');
    expect(body).not.toHaveProperty('price');
    expect(body).not.toHaveProperty('totalAmount');
    expect(body).not.toHaveProperty('currency');
    expect(body).not.toHaveProperty('status');
    expect(body).not.toHaveProperty('expiresAtUtc');
    expect(body).not.toHaveProperty('addOnAmenityIds');

    const reservation: Reservation = {
      id: 'reservation-1',
      buildingId: 'building-1',
      useType: 'ExclusiveLeisure',
      status: 'Pending',
      startsAtUtc: '2026-01-05T10:00:00Z',
      endsAtUtc: '2026-01-05T12:00:00Z',
      createdAtUtc: '2026-01-01T00:00:00Z',
      expiresAtUtc: '2026-01-01T00:30:00Z',
      resources: [{ amenityId: 'amenity-1', isExclusive: true }],
      priceLines: [
        { amenityId: 'amenity-1', componentType: 'Base', currency: 'ARS', amount: 6500 }
      ],
      currency: 'ARS',
      totalAmount: 6500
    };
    request.flush(reservation);

    await expect(promise).resolves.toEqual(reservation);
  });
});
