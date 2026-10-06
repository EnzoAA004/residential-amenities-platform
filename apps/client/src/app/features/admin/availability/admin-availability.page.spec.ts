import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';

import { API_BASE_URL } from '../../../core/config/api-base-url.token';
import { AdminAvailabilityConfig } from '../admin.models';
import { AdminAvailabilityPage } from './admin-availability.page';

// Example schedule only — not a real operating policy.
const config: AdminAvailabilityConfig = {
  amenityId: 'amenity-1',
  buildingId: 'building-1',
  windows: [{ id: 'window-1', dayOfWeek: 1, startTime: '10:00:00', endTime: '12:00:00' }],
  unavailablePeriods: []
};

describe('AdminAvailabilityPage', () => {
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([{ path: 'admin/availability', component: AdminAvailabilityPage }]),
        { provide: API_BASE_URL, useValue: '/api' }
      ]
    });

    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  async function navigate() {
    const harness = await RouterTestingHarness.create();
    const component = await harness.navigateByUrl('/admin/availability', AdminAvailabilityPage);
    return { harness, component };
  }

  function text(harness: RouterTestingHarness): string {
    return harness.routeNativeElement!.textContent!;
  }

  async function loadConfig(harness: RouterTestingHarness, component: AdminAvailabilityPage) {
    component.draftBuildingId.set('building-1');
    component.draftAmenityId.set('amenity-1');
    component.load(new Event('submit'));

    const request = httpMock.expectOne(
      (r) => r.url === '/api/admin/amenities/amenity-1/availability' && r.method === 'GET'
    );
    expect(request.request.params.get('buildingId')).toBe('building-1');
    request.flush(config);
    await harness.fixture.whenStable();
    harness.detectChanges();
  }

  it('loads the availability configuration for the given amenity/building', async () => {
    const { harness, component } = await navigate();
    await loadConfig(harness, component);

    expect(text(harness)).toContain('Ventanas semanales');
    expect(text(harness)).toContain('Estos cambios afectan disponibilidad futura; no cancelan reservas existentes.');
  });

  it('replaces the weekly windows and refreshes from the response', async () => {
    const { harness, component } = await navigate();
    await loadConfig(harness, component);

    component.setRowStart(0, '09:00');
    component.setRowEnd(0, '11:00');
    component.submitReplace();

    const request = httpMock.expectOne('/api/admin/amenities/amenity-1/availability');
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({
      buildingId: 'building-1',
      windows: [{ dayOfWeek: 1, startTime: '09:00:00', endTime: '11:00:00' }]
    });

    const updated: AdminAvailabilityConfig = {
      ...config,
      windows: [{ id: 'window-1', dayOfWeek: 1, startTime: '09:00:00', endTime: '11:00:00' }]
    };
    request.flush(updated);
    await harness.fixture.whenStable();
    harness.detectChanges();

    httpMock.expectNone((r) => r.method === 'GET' && r.url.includes('availability'));
  });

  it('blocks a window whose end is not after its start, without calling the backend', async () => {
    const { harness, component } = await navigate();
    await loadConfig(harness, component);

    component.setRowStart(0, '12:00');
    component.setRowEnd(0, '10:00');
    component.submitReplace();

    httpMock.expectNone((r) => r.method === 'PUT');
  });

  it('blocks an overnight window, without calling the backend', async () => {
    const { harness, component } = await navigate();
    await loadConfig(harness, component);

    component.setRowStart(0, '22:00');
    component.setRowEnd(0, '06:00');
    component.submitReplace();

    httpMock.expectNone((r) => r.method === 'PUT');
  });

  it('shows the real backend detail for a 422 overlap rejection', async () => {
    const { harness, component } = await navigate();
    await loadConfig(harness, component);

    component.submitReplace();
    httpMock.expectOne('/api/admin/amenities/amenity-1/availability').flush(
      { status: 422, title: 'Unable to complete this operation.', detail: 'Windows for the same day must not overlap.' },
      { status: 422, statusText: 'Unprocessable Entity' }
    );
    await harness.fixture.whenStable();
    harness.detectChanges();

    expect(text(harness)).toContain('Windows for the same day must not overlap.');
  });

  it('adds a maintenance period and refreshes the configuration', async () => {
    const { harness, component } = await navigate();
    await loadConfig(harness, component);

    component.setPeriodField('startsAtUtc', '2026-10-05T09:00');
    component.setPeriodField('endsAtUtc', '2026-10-05T18:00');
    component.setPeriodField('reason', 'resurfacing');
    component.submitPeriod(new Event('submit'));

    const created = httpMock.expectOne('/api/admin/amenities/amenity-1/unavailable-periods');
    expect(created.request.method).toBe('POST');
    expect(created.request.body.buildingId).toBe('building-1');
    expect(created.request.body.reason).toBe('resurfacing');
    created.flush({ periodId: 'period-1' }, { status: 201, statusText: 'Creard' });

    const refreshed = httpMock.expectOne(
      (r) => r.url === '/api/admin/amenities/amenity-1/availability' && r.method === 'GET'
    );
    refreshed.flush({
      ...config,
      unavailablePeriods: [{ id: 'period-1', startsAtUtc: '2026-10-05T09:00:00Z', endsAtUtc: '2026-10-05T18:00:00Z', reason: 'resurfacing' }]
    });
    await harness.fixture.whenStable();
    harness.detectChanges();

    expect(text(harness)).toContain('resurfacing');
  });

  it('removes a maintenance period and refreshes the configuration', async () => {
    const { harness, component } = await navigate();
    component.draftBuildingId.set('building-1');
    component.draftAmenityId.set('amenity-1');
    component.load(new Event('submit'));
    httpMock
      .expectOne((r) => r.url === '/api/admin/amenities/amenity-1/availability' && r.method === 'GET')
      .flush({
        ...config,
        unavailablePeriods: [{ id: 'period-1', startsAtUtc: '2026-10-05T09:00:00Z', endsAtUtc: '2026-10-05T18:00:00Z', reason: 'resurfacing' }]
      });
    await harness.fixture.whenStable();
    harness.detectChanges();

    component.deletePeriod('period-1');

    const deleted = httpMock.expectOne(
      (r) => r.url === '/api/admin/amenities/amenity-1/unavailable-periods/period-1' && r.method === 'DELETE'
    );
    expect(deleted.request.params.get('buildingId')).toBe('building-1');
    deleted.flush(null, { status: 204, statusText: 'No Content' });

    const refreshed = httpMock.expectOne(
      (r) => r.url === '/api/admin/amenities/amenity-1/availability' && r.method === 'GET'
    );
    refreshed.flush({ ...config, unavailablePeriods: [] });
    await harness.fixture.whenStable();
    harness.detectChanges();

    expect(text(harness)).toContain('No hay períodos configurados.');
  });
});
