import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';

import { API_BASE_URL } from '../../../core/config/api-base-url.token';
import { AdminEventSlot } from '../admin.models';
import { AdminEventSlotsPage } from './admin-event-slots.page';

// Example times only — not real production hours.
const activeSlot: AdminEventSlot = {
  id: 'slot-1',
  buildingId: 'building-1',
  name: 'Example brunch',
  startTime: '10:00:00',
  endTime: '13:00:00',
  isOvernight: false,
  isActive: true
};

const inactiveSlot: AdminEventSlot = {
  id: 'slot-2',
  buildingId: 'building-1',
  name: 'Example late slot',
  startTime: '20:00:00',
  endTime: '23:00:00',
  isOvernight: false,
  isActive: false
};

describe('AdminEventSlotsPage', () => {
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([{ path: 'admin/event-slots', component: AdminEventSlotsPage }]),
        { provide: API_BASE_URL, useValue: '/api' }
      ]
    });

    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  async function navigate() {
    const harness = await RouterTestingHarness.create();
    const component = await harness.navigateByUrl('/admin/event-slots', AdminEventSlotsPage);
    return { harness, component };
  }

  function text(harness: RouterTestingHarness): string {
    return harness.routeNativeElement!.textContent!;
  }

  async function loadSlots(harness: RouterTestingHarness, component: AdminEventSlotsPage) {
    component.draftBuildingId.set('building-1');
    component.load(new Event('submit'));

    const request = httpMock.expectOne('/api/admin/buildings/building-1/event-slots');
    expect(request.request.method).toBe('GET');
    request.flush([activeSlot, inactiveSlot]);
    await harness.fixture.whenStable();
    harness.detectChanges();
  }

  it('lists both active and inactive slots', async () => {
    const { harness, component } = await navigate();
    await loadSlots(harness, component);

    expect(text(harness)).toContain('Example brunch');
    expect(text(harness)).toContain('Activo');
    expect(text(harness)).toContain('Example late slot');
    expect(text(harness)).toContain('Inactivo');
  });

  it('creates a new slot', async () => {
    const { harness, component } = await navigate();
    await loadSlots(harness, component);

    component.setField('name', 'Example workshop');
    component.setField('startTime', '09:00');
    component.setField('endTime', '10:00');
    component.submit(new Event('submit'));

    const created = httpMock.expectOne('/api/admin/buildings/building-1/event-slots');
    expect(created.request.method).toBe('POST');
    expect(created.request.body).toEqual({ name: 'Example workshop', startTime: '09:00:00', endTime: '10:00:00' });
    created.flush(
      { id: 'slot-3', buildingId: 'building-1', name: 'Example workshop', startTime: '09:00:00', endTime: '10:00:00', isActive: true },
      { status: 201, statusText: 'Created' }
    );

    httpMock.expectOne('/api/admin/buildings/building-1/event-slots').flush([activeSlot, inactiveSlot]);
    await harness.fixture.whenStable();
    harness.detectChanges();
  });

  it('updates an existing slot via PUT, not a new create', async () => {
    const { harness, component } = await navigate();
    await loadSlots(harness, component);

    component.startEdit(activeSlot);
    component.setField('name', 'Example brunch (updated)');
    component.submit(new Event('submit'));

    const updated = httpMock.expectOne('/api/admin/event-slots/slot-1');
    expect(updated.request.method).toBe('PUT');
    expect(updated.request.body.name).toBe('Example brunch (updated)');
    updated.flush({ ...activeSlot, name: 'Example brunch (updated)' });

    httpMock.expectOne('/api/admin/buildings/building-1/event-slots').flush([activeSlot, inactiveSlot]);
    await harness.fixture.whenStable();
    harness.detectChanges();
  });

  it('deactivates an active slot', async () => {
    const { harness, component } = await navigate();
    await loadSlots(harness, component);

    component.deactivate(activeSlot);

    const request = httpMock.expectOne('/api/admin/event-slots/slot-1/deactivate');
    expect(request.request.method).toBe('POST');
    request.flush({ ...activeSlot, isActive: false });

    httpMock.expectOne('/api/admin/buildings/building-1/event-slots').flush([{ ...activeSlot, isActive: false }, inactiveSlot]);
    await harness.fixture.whenStable();
    harness.detectChanges();
  });

  it('reactivates an inactive slot only through an explicit, separate action', async () => {
    const { harness, component } = await navigate();
    await loadSlots(harness, component);

    component.activate(inactiveSlot);

    const request = httpMock.expectOne('/api/admin/event-slots/slot-2/activate');
    expect(request.request.method).toBe('POST');
    request.flush({ ...inactiveSlot, isActive: true });

    httpMock.expectOne('/api/admin/buildings/building-1/event-slots').flush([activeSlot, { ...inactiveSlot, isActive: true }]);
    await harness.fixture.whenStable();
    harness.detectChanges();
  });

  it('blocks an invalid (end<=start) range client-side, without calling the backend', async () => {
    const { harness, component } = await navigate();
    await loadSlots(harness, component);

    component.setField('name', 'Example bad slot');
    component.setField('startTime', '12:00');
    component.setField('endTime', '10:00');
    component.submit(new Event('submit'));

    httpMock.expectNone((r) => r.method === 'POST' && r.url.includes('event-slots'));
  });

  it('blocks an end-before-start range client-side without the overnight flag', async () => {
    const { harness, component } = await navigate();
    await loadSlots(harness, component);

    component.setField('name', 'Example overnight slot');
    component.setField('startTime', '22:00');
    component.setField('endTime', '02:00');
    component.submit(new Event('submit'));

    httpMock.expectNone((r) => r.method === 'POST' && r.url.includes('event-slots'));
  });

  it('sends isOvernight and allows end-before-start once the flag is set (DEC-014/OQ-002)', async () => {
    const { harness, component } = await navigate();
    await loadSlots(harness, component);

    component.setField('name', 'Night');
    component.setField('startTime', '20:00');
    component.setField('endTime', '03:00');
    component.setOvernight(true);
    component.submit(new Event('submit'));

    const request = httpMock.expectOne((r) => r.method === 'POST' && r.url.includes('event-slots'));
    expect(request.request.body).toEqual({
      name: 'Night',
      startTime: '20:00:00',
      endTime: '03:00:00',
      isOvernight: true
    });
    request.flush({ ...activeSlot, id: 'slot-3', name: 'Night', startTime: '20:00:00', endTime: '03:00:00', isOvernight: true });

    const listRequest = httpMock.expectOne((r) => r.method === 'GET' && r.url.includes('event-slots'));
    listRequest.flush([activeSlot]);
  });

  it('still rejects an overnight-flagged slot whose end is not before its start', async () => {
    const { harness, component } = await navigate();
    await loadSlots(harness, component);

    component.setField('name', 'Bad overnight');
    component.setField('startTime', '10:00');
    component.setField('endTime', '11:00');
    component.setOvernight(true);
    component.submit(new Event('submit'));

    httpMock.expectNone((r) => r.method === 'POST' && r.url.includes('event-slots'));
  });

  it('never offers a delete action anywhere in the UI', async () => {
    const { harness, component } = await navigate();
    await loadSlots(harness, component);

    expect(text(harness).toLowerCase()).not.toContain('eliminar');
    expect((component as unknown as Record<string, unknown>)['deleteEventSlot']).toBeUndefined();
    expect((component as unknown as Record<string, unknown>)['delete']).toBeUndefined();
  });
});
