import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';

import { API_BASE_URL } from '../../../core/config/api-base-url.token';
import { AuthSessionStore } from '../../../core/auth/auth-session.store';
import { CurrentUser } from '../../../core/auth/auth.models';
import { ResidentContextStore } from '../../../core/resident-context/resident-context.store';
import { AmenitiesPage } from './amenities.page';

const buildingA = { buildingId: 'building-a', unitId: 'unit-a', unit: '1A', building: 'Building A' };
const buildingB = { buildingId: 'building-b', unitId: 'unit-b', unit: '2B', building: 'Building B' };

function userWith(memberships: CurrentUser['memberships']): CurrentUser {
  return {
    id: 'user-1',
    email: 'resident@example.test',
    displayName: 'Resident One',
    roles: ['Resident'],
    memberships
  };
}

describe('AmenitiesPage', () => {
  let fixture: ComponentFixture<AmenitiesPage>;
  let session: AuthSessionStore;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [AmenitiesPage],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: API_BASE_URL, useValue: '/api' },
        AuthSessionStore,
        ResidentContextStore
      ]
    });

    session = TestBed.inject(AuthSessionStore);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('shows an empty-membership message and requests nothing with 0 memberships', () => {
    session.setAuthenticated(userWith([]));
    fixture = TestBed.createComponent(AmenitiesPage);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain(
      'No tenés una membresía residencial activa disponible.'
    );
    httpMock.expectNone(() => true);
  });

  it('auto-loads amenities for a single membership without a selector', () => {
    session.setAuthenticated(userWith([buildingA]));
    fixture = TestBed.createComponent(AmenitiesPage);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('ion-select')).toBeNull();

    const request = httpMock.expectOne('/api/buildings/building-a/amenities');
    request.flush([]);
  });

  it('shows a building selector with several memberships and does not request until one is chosen', () => {
    session.setAuthenticated(userWith([buildingA, buildingB]));
    fixture = TestBed.createComponent(AmenitiesPage);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('ion-select')).not.toBeNull();
    httpMock.expectNone(() => true);
  });

  it('loads amenities once a building is selected, and shows the empty-amenities state for []', async () => {
    session.setAuthenticated(userWith([buildingA, buildingB]));
    fixture = TestBed.createComponent(AmenitiesPage);
    fixture.detectChanges();

    fixture.componentInstance.onBuildingChange(
      new CustomEvent('ionChange', { detail: { value: 'building-a' } })
    );
    fixture.detectChanges();

    const request = httpMock.expectOne('/api/buildings/building-a/amenities');
    request.flush([]);
    await fixture.whenStable();
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Este edificio no tiene espacios comunes activos.');
  });

  it('renders amenities on success and lets the resident select one', async () => {
    session.setAuthenticated(userWith([buildingA]));
    fixture = TestBed.createComponent(AmenitiesPage);
    fixture.detectChanges();

    const request = httpMock.expectOne('/api/buildings/building-a/amenities');
    request.flush([
      { id: 'amenity-1', name: 'Pool', kind: 'Leisure', allowsSharedUse: true, allowsExclusiveUse: false }
    ]);
    await fixture.whenStable();
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Pool');

    fixture.componentInstance.selectAmenity({
      id: 'amenity-1',
      name: 'Pool',
      kind: 'Leisure',
      allowsSharedUse: true,
      allowsExclusiveUse: false
    });
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('app-amenity-availability')).not.toBeNull();
  });

  it('shows an access-denied message on 403 without signing the user out', async () => {
    session.setAuthenticated(userWith([buildingA]));
    fixture = TestBed.createComponent(AmenitiesPage);
    fixture.detectChanges();

    const request = httpMock.expectOne('/api/buildings/building-a/amenities');
    request.flush(
      { status: 403, title: 'You do not have access to this building.' },
      { status: 403, statusText: 'Forbidden' }
    );
    await fixture.whenStable();
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('You do not have access to this building.');
    expect(session.isAuthenticated()).toBe(true);
  });

  it('retries after a network/5xx error', async () => {
    session.setAuthenticated(userWith([buildingA]));
    fixture = TestBed.createComponent(AmenitiesPage);
    fixture.detectChanges();

    httpMock.expectOne('/api/buildings/building-a/amenities').flush(null, { status: 500, statusText: 'Error' });
    await fixture.whenStable();
    fixture.detectChanges();

    fixture.componentInstance.retryAmenities();
    fixture.detectChanges();

    const retried = httpMock.expectOne('/api/buildings/building-a/amenities');
    retried.flush([]);
  });

  it('clears the selected amenity when the active building changes', async () => {
    session.setAuthenticated(userWith([buildingA, buildingB]));
    fixture = TestBed.createComponent(AmenitiesPage);
    fixture.detectChanges();

    fixture.componentInstance.onBuildingChange(
      new CustomEvent('ionChange', { detail: { value: 'building-a' } })
    );
    fixture.detectChanges();
    httpMock
      .expectOne('/api/buildings/building-a/amenities')
      .flush([{ id: 'amenity-1', name: 'Pool', kind: 'Leisure', allowsSharedUse: true, allowsExclusiveUse: false }]);
    await fixture.whenStable();

    fixture.componentInstance.selectAmenity({
      id: 'amenity-1',
      name: 'Pool',
      kind: 'Leisure',
      allowsSharedUse: true,
      allowsExclusiveUse: false
    });
    expect(fixture.componentInstance.selectedAmenity()).not.toBeNull();

    fixture.componentInstance.onBuildingChange(
      new CustomEvent('ionChange', { detail: { value: 'building-b' } })
    );
    fixture.detectChanges();

    expect(fixture.componentInstance.selectedAmenity()).toBeNull();
    httpMock.expectOne('/api/buildings/building-b/amenities').flush([]);
  });

  it('ignores a stale amenities response after the building changes (no request piles the old UI)', async () => {
    session.setAuthenticated(userWith([buildingA, buildingB]));
    fixture = TestBed.createComponent(AmenitiesPage);
    fixture.detectChanges();

    fixture.componentInstance.onBuildingChange(
      new CustomEvent('ionChange', { detail: { value: 'building-a' } })
    );
    fixture.detectChanges();
    const staleRequest = httpMock.expectOne('/api/buildings/building-a/amenities');

    fixture.componentInstance.onBuildingChange(
      new CustomEvent('ionChange', { detail: { value: 'building-b' } })
    );
    fixture.detectChanges();
    const freshRequest = httpMock.expectOne('/api/buildings/building-b/amenities');

    // switchMap already unsubscribed from the stale request when the
    // building changed, so flushing it now throws instead of updating the UI.
    expect(() =>
      staleRequest.flush([
        { id: 'amenity-1', name: 'Stale Pool', kind: 'Leisure', allowsSharedUse: true, allowsExclusiveUse: false }
      ])
    ).toThrow();

    freshRequest.flush([
      { id: 'amenity-2', name: 'Fresh Gym', kind: 'Fitness', allowsSharedUse: true, allowsExclusiveUse: false }
    ]);
    await fixture.whenStable();
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Fresh Gym');
  });

  it('does not render the Event flow for a non-SUM amenity', async () => {
    session.setAuthenticated(userWith([buildingA]));
    fixture = TestBed.createComponent(AmenitiesPage);
    fixture.detectChanges();

    httpMock.expectOne('/api/buildings/building-a/amenities').flush([
      { id: 'pool-1', name: 'Pool', kind: 'Pool', allowsSharedUse: true, allowsExclusiveUse: true }
    ]);
    await fixture.whenStable();
    fixture.detectChanges();

    fixture.componentInstance.selectAmenity({
      id: 'pool-1',
      name: 'Pool',
      kind: 'Pool',
      allowsSharedUse: true,
      allowsExclusiveUse: true
    });
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('app-event-reservation')).toBeNull();
  });

  it('renders the Event flow, with the full amenities list as add-on candidates, for a SUM amenity', async () => {
    session.setAuthenticated(userWith([buildingA]));
    fixture = TestBed.createComponent(AmenitiesPage);
    fixture.detectChanges();

    const sum = { id: 'sum-1', name: 'SUM', kind: 'Sum', allowsSharedUse: true, allowsExclusiveUse: true };
    const pool = { id: 'pool-1', name: 'Pool', kind: 'Pool', allowsSharedUse: true, allowsExclusiveUse: true };
    httpMock.expectOne('/api/buildings/building-a/amenities').flush([sum, pool]);
    await fixture.whenStable();
    fixture.detectChanges();

    fixture.componentInstance.selectAmenity(sum);
    fixture.detectChanges();

    const eventComponent = fixture.nativeElement.querySelector('app-event-reservation');
    expect(eventComponent).not.toBeNull();
  });
});
