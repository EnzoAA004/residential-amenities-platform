import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';

import { API_BASE_URL } from '../../../core/config/api-base-url.token';
import { AmenityAvailabilityComponent } from './amenity-availability.component';
import { AmenitySummary } from './amenities.models';

const pool: AmenitySummary = {
  id: 'amenity-1',
  name: 'Pool',
  kind: 'Leisure',
  allowsSharedUse: true,
  allowsExclusiveUse: false
};

const gym: AmenitySummary = {
  id: 'amenity-2',
  name: 'Gym',
  kind: 'Fitness',
  allowsSharedUse: true,
  allowsExclusiveUse: false
};

describe('AmenityAvailabilityComponent', () => {
  let fixture: ComponentFixture<AmenityAvailabilityComponent>;
  let component: AmenityAvailabilityComponent;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [AmenityAvailabilityComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: API_BASE_URL, useValue: '/api' }
      ]
    });

    fixture = TestBed.createComponent(AmenityAvailabilityComponent);
    component = fixture.componentInstance;
    component.amenity = pool;
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('starts idle and does not request anything until the user searches', () => {
    fixture.detectChanges();

    expect(component.state()).toEqual({ status: 'idle' });
    httpMock.expectNone(() => true);
  });

  it('does not fire a request for an invalid range (end before start)', () => {
    fixture.detectChanges();

    component.form.controls.from.setValue('2026-01-10T10:00');
    component.form.controls.to.setValue('2026-01-01T10:00');
    component.search();

    expect(component.rangeError()).toBeTruthy();
    httpMock.expectNone(() => true);
  });

  it('does not fire a request for a range longer than 62 days', () => {
    fixture.detectChanges();

    component.form.controls.from.setValue('2026-01-01T00:00');
    component.form.controls.to.setValue('2026-04-15T00:00');
    component.search();

    expect(component.rangeError()).toContain('62');
    httpMock.expectNone(() => true);
  });

  it('renders intervals grouped by day on success', async () => {
    fixture.detectChanges();

    component.form.controls.from.setValue('2026-01-05T00:00');
    component.form.controls.to.setValue('2026-01-06T00:00');
    component.search();

    const request = httpMock.expectOne((req) => req.url === '/api/amenities/amenity-1/availability');
    request.flush([{ startUtc: '2026-01-05T10:00:00Z', endUtc: '2026-01-05T13:00:00Z' }]);
    await fixture.whenStable();

    expect(component.state().status).toBe('success');
    expect(component.groupedDays()).toHaveLength(1);
  });

  it('treats an empty [] response as a valid, non-error state', async () => {
    fixture.detectChanges();

    component.search();

    const request = httpMock.expectOne((req) => req.url === '/api/amenities/amenity-1/availability');
    request.flush([]);
    await fixture.whenStable();

    expect(component.state()).toEqual({ status: 'empty' });
  });

  it('resets to idle and does not request when the amenity changes', () => {
    fixture.detectChanges();

    component.search();
    httpMock.expectOne(() => true).flush([]);

    component.amenity = gym;

    expect(component.state()).toEqual({ status: 'idle' });
    httpMock.expectNone(() => true);
  });

  it('cancels a stale response when the amenity changes before it resolves', async () => {
    fixture.detectChanges();

    component.search();
    const firstRequest = httpMock.expectOne(
      (req) => req.url === '/api/amenities/amenity-1/availability'
    );

    // Switch amenity before the first request resolves: switchMap
    // unsubscribes from the stale request, so it can no longer be flushed.
    component.amenity = gym;
    component.search();
    const secondRequest = httpMock.expectOne(
      (req) => req.url === '/api/amenities/amenity-2/availability'
    );

    expect(() =>
      firstRequest.flush([{ startUtc: '2026-01-05T10:00:00Z', endUtc: '2026-01-05T13:00:00Z' }])
    ).toThrow();

    secondRequest.flush([]);
    await fixture.whenStable();

    expect(component.state()).toEqual({ status: 'empty' });
  });
});
