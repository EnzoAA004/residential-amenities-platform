import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';

import { AuthSessionStore } from '../../../../core/auth/auth-session.store';
import { CurrentUser } from '../../../../core/auth/auth.models';
import { API_BASE_URL } from '../../../../core/config/api-base-url.token';
import { ResidentContextStore } from '../../../../core/resident-context/resident-context.store';
import { ResidentReservationListPage } from './resident-reservation-list.page';
import { ResidentReservationPage, ResidentReservationSummary } from './resident-reservation.models';

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

function summaryWith(overrides: Partial<ResidentReservationSummary> = {}): ResidentReservationSummary {
  return {
    id: 'reservation-1',
    buildingId: 'building-a',
    useType: 'SharedLeisure',
    status: 'Pending',
    startsAtUtc: '2026-10-01T13:00:00Z',
    endsAtUtc: '2026-10-01T14:00:00Z',
    createdAtUtc: '2026-10-01T12:30:00Z',
    expiresAtUtc: '2026-10-01T13:00:00Z',
    confirmedAtUtc: null,
    cancelledAtUtc: null,
    expiredAtUtc: null,
    cancellationReason: null,
    resources: [],
    currency: 'ARS',
    totalAmount: 5000,
    ...overrides
  };
}

function pageWith(overrides: Partial<ResidentReservationPage> = {}): ResidentReservationPage {
  return {
    items: [summaryWith()],
    page: 1,
    pageSize: 50,
    totalCount: 1,
    ...overrides
  };
}

describe('ResidentReservationListPage', () => {
  let fixture: ComponentFixture<ResidentReservationListPage>;
  let session: AuthSessionStore;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [ResidentReservationListPage],
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

  it('shows the no-membership state and makes no HTTP request', () => {
    session.setAuthenticated(userWith([]));
    fixture = TestBed.createComponent(ResidentReservationListPage);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain(
      'No tenés una membresía residencial activa disponible.'
    );
    httpMock.expectNone(() => true);
  });

  it('uses buildingId from ResidentContextStore.activeMembership only', () => {
    session.setAuthenticated(userWith([buildingA]));
    fixture = TestBed.createComponent(ResidentReservationListPage);
    fixture.detectChanges();

    const request = httpMock.expectOne(
      (req) =>
        req.url === '/api/reservations' &&
        req.params.get('buildingId') === 'building-a' &&
        req.params.get('page') === '1' &&
        req.params.get('pageSize') === '50'
    );
    expect(request.request.params.has('membershipId')).toBe(false);
    expect(request.request.params.has('userId')).toBe(false);
    expect(request.request.params.has('createdByMembershipId')).toBe(false);
    expect(request.request.url).not.toContain('/admin/');
    request.flush(pageWith());
  });

  it('renders empty state and real pagination metadata', async () => {
    session.setAuthenticated(userWith([buildingA]));
    fixture = TestBed.createComponent(ResidentReservationListPage);
    fixture.detectChanges();

    httpMock.expectOne('/api/reservations?buildingId=building-a&page=1&pageSize=50').flush(
      pageWith({ items: [], page: 1, pageSize: 50, totalCount: 0 })
    );
    await fixture.whenStable();
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Todavía no tenés reservas');
    expect(fixture.nativeElement.textContent).toContain('Página 1 · 0 reservas');
  });

  it('renders reservations and requests the next page with page/pageSize/totalCount intact', async () => {
    session.setAuthenticated(userWith([buildingA]));
    fixture = TestBed.createComponent(ResidentReservationListPage);
    fixture.detectChanges();

    httpMock
      .expectOne('/api/reservations?buildingId=building-a&page=1&pageSize=50')
      .flush(pageWith({ totalCount: 75 }));
    await fixture.whenStable();
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('SharedLeisure');
    expect(fixture.nativeElement.textContent).toContain('Pending');

    fixture.componentInstance.nextPage();
    fixture.detectChanges();

    httpMock
      .expectOne('/api/reservations?buildingId=building-a&page=2&pageSize=50')
      .flush(pageWith({ page: 2, totalCount: 75, items: [summaryWith({ id: 'reservation-2' })] }));
  });

  it('cancels a stale building request and never renders previous-building data', async () => {
    session.setAuthenticated(userWith([buildingA, buildingB]));
    fixture = TestBed.createComponent(ResidentReservationListPage);
    fixture.detectChanges();
    httpMock.expectNone(() => true);

    fixture.componentInstance.onBuildingChange(
      new CustomEvent('ionChange', { detail: { value: 'building-a' } })
    );
    fixture.detectChanges();
    const staleRequest = httpMock.expectOne('/api/reservations?buildingId=building-a&page=1&pageSize=50');

    fixture.componentInstance.onBuildingChange(
      new CustomEvent('ionChange', { detail: { value: 'building-b' } })
    );
    fixture.detectChanges();
    const freshRequest = httpMock.expectOne('/api/reservations?buildingId=building-b&page=1&pageSize=50');

    expect(() =>
      staleRequest.flush(pageWith({ items: [summaryWith({ useType: 'Stale reservation' })] }))
    ).toThrow();

    freshRequest.flush(
      pageWith({ items: [summaryWith({ buildingId: 'building-b', useType: 'Fresh reservation' })] })
    );
    await fixture.whenStable();
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Fresh reservation');
    expect(fixture.nativeElement.textContent).not.toContain('Stale reservation');
  });
});
