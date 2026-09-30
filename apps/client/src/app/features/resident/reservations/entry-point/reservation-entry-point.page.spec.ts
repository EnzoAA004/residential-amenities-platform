import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { convertToParamMap, provideRouter } from '@angular/router';
import { ActivatedRoute } from '@angular/router';
import { BehaviorSubject } from 'rxjs';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';

import { AuthSessionStore } from '../../../../core/auth/auth-session.store';
import { CurrentUser } from '../../../../core/auth/auth.models';
import { API_BASE_URL } from '../../../../core/config/api-base-url.token';
import { ResidentContextStore } from '../../../../core/resident-context/resident-context.store';
import { ReservationEntryPoint } from './reservation-entry-point.models';
import { ReservationEntryPointPage } from './reservation-entry-point.page';

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

function entryPoint(overrides: Partial<ReservationEntryPoint> = {}): ReservationEntryPoint {
  return {
    token: 'pilot-sum',
    buildingId: 'building-a',
    amenityId: 'sum-1',
    displayName: 'Pilot SUM',
    amenityName: 'SUM',
    amenityKind: 'Sum',
    allowsSharedUse: true,
    allowsExclusiveUse: true,
    suggestedUseType: 'Event',
    ...overrides
  };
}

describe('ReservationEntryPointPage', () => {
  let fixture: ComponentFixture<ReservationEntryPointPage>;
  let session: AuthSessionStore;
  let residentContext: ResidentContextStore;
  let httpMock: HttpTestingController;
  let paramMap: BehaviorSubject<ReturnType<typeof convertToParamMap>>;

  beforeEach(() => {
    paramMap = new BehaviorSubject(convertToParamMap({ token: 'pilot-sum' }));

    TestBed.configureTestingModule({
      imports: [ReservationEntryPointPage],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: API_BASE_URL, useValue: '/api' },
        {
          provide: ActivatedRoute,
          useValue: { paramMap: paramMap.asObservable() }
        },
        AuthSessionStore,
        ResidentContextStore
      ]
    });

    session = TestBed.inject(AuthSessionStore);
    residentContext = TestBed.inject(ResidentContextStore);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('resolves the token, activates its building and reuses existing reservation flows', async () => {
    session.setAuthenticated(userWith([buildingA, buildingB]));
    fixture = TestBed.createComponent(ReservationEntryPointPage);
    fixture.detectChanges();

    httpMock.expectOne('/api/reservation-entry-points/pilot-sum').flush(entryPoint());
    await fixture.whenStable();
    fixture.detectChanges();

    expect(residentContext.activeBuildingId()).toBe('building-a');
    expect(fixture.nativeElement.textContent).toContain('Pilot SUM');
    expect(fixture.nativeElement.querySelector('app-amenity-availability')).not.toBeNull();

    httpMock.expectOne('/api/buildings/building-a/amenities').flush([
      { id: 'sum-1', name: 'SUM', kind: 'Sum', allowsSharedUse: true, allowsExclusiveUse: true },
      { id: 'pool-1', name: 'Pool', kind: 'Pool', allowsSharedUse: true, allowsExclusiveUse: true }
    ]);
    await fixture.whenStable();
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('app-event-reservation')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('app-leisure-reservation')).not.toBeNull();
  });

  it('renders a clear invalid/inactive QR state on 404', async () => {
    session.setAuthenticated(userWith([buildingA]));
    fixture = TestBed.createComponent(ReservationEntryPointPage);
    fixture.detectChanges();

    httpMock
      .expectOne('/api/reservation-entry-points/pilot-sum')
      .flush({ status: 404, title: 'Not Found' }, { status: 404, statusText: 'Not Found' });
    await fixture.whenStable();
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('QR inválido o inactivo.');
    httpMock.expectNone('/api/buildings/building-a/amenities');
  });

  it('renders a clear access state on 403 and does not show reservation flows', async () => {
    session.setAuthenticated(userWith([buildingA]));
    fixture = TestBed.createComponent(ReservationEntryPointPage);
    fixture.detectChanges();

    httpMock
      .expectOne('/api/reservation-entry-points/pilot-sum')
      .flush(
        { status: 403, title: 'You do not have access to this building.' },
        { status: 403, statusText: 'Forbidden' }
      );
    await fixture.whenStable();
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('No tenés acceso a este edificio.');
    expect(fixture.nativeElement.querySelector('app-leisure-reservation')).toBeNull();
    expect(fixture.nativeElement.querySelector('app-event-reservation')).toBeNull();
  });

  it('does not trust a resolved building missing from the local resident memberships', async () => {
    session.setAuthenticated(userWith([buildingA]));
    fixture = TestBed.createComponent(ReservationEntryPointPage);
    fixture.detectChanges();

    httpMock
      .expectOne('/api/reservation-entry-points/pilot-sum')
      .flush(entryPoint({ buildingId: 'building-b' }));
    await fixture.whenStable();
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('No pudimos activar el edificio de este QR');
    expect(fixture.nativeElement.querySelector('app-leisure-reservation')).toBeNull();
    httpMock.expectNone('/api/buildings/building-b/amenities');
  });

  it('shows not-reservable state when the resolved amenity has no supported reservation mode', async () => {
    session.setAuthenticated(userWith([buildingA]));
    fixture = TestBed.createComponent(ReservationEntryPointPage);
    fixture.detectChanges();

    httpMock
      .expectOne('/api/reservation-entry-points/pilot-sum')
      .flush(
        entryPoint({
          amenityKind: 'Gym',
          allowsSharedUse: false,
          allowsExclusiveUse: false,
          suggestedUseType: null
        })
      );
    await fixture.whenStable();
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Este recurso no tiene un flujo de reserva disponible.');
    expect(fixture.nativeElement.querySelector('app-leisure-reservation')).toBeNull();
    expect(fixture.nativeElement.querySelector('app-event-reservation')).toBeNull();
  });
});
