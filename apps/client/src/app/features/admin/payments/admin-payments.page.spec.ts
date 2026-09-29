import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';

import { API_BASE_URL } from '../../../core/config/api-base-url.token';
import { AdminPayment } from '../admin.models';
import { AdminPaymentsPage } from './admin-payments.page';

const payment: AdminPayment = {
  paymentId: 'payment-1',
  reservationId: 'reservation-1',
  buildingId: 'building-1',
  method: 'MercadoPago',
  status: 'Approved',
  amount: 12000,
  currency: 'ARS',
  createdAtUtc: '2026-10-01T10:05:00Z',
  approvedAtUtc: '2026-10-01T10:06:00Z',
  reservationOutcome: 'ApprovedAfterExpiry',
  requiresManualReview: true,
  providerStatus: 'approved',
  providerStatusDetail: 'accredited',
  cashDeclaredAtUtc: null,
  cashConfirmedAtUtc: null,
  cashConfirmedByUserId: null
};

describe('AdminPaymentsPage', () => {
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([{ path: 'admin/payments', component: AdminPaymentsPage }]),
        { provide: API_BASE_URL, useValue: '/api' }
      ]
    });

    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  async function navigate() {
    const harness = await RouterTestingHarness.create();
    const component = await harness.navigateByUrl('/admin/payments', AdminPaymentsPage);
    return { harness, component };
  }

  function text(harness: RouterTestingHarness): string {
    return harness.routeNativeElement!.textContent!;
  }

  it('requests the default page and renders payment status/method/manual review', async () => {
    const { harness } = await navigate();
    httpMock.expectOne('/api/admin/payments?page=1&pageSize=50').flush({
      items: [payment],
      page: 1,
      pageSize: 50,
      totalCount: 51
    });
    await harness.fixture.whenStable();
    harness.detectChanges();

    expect(text(harness)).toContain('Página 1');
    expect(text(harness)).toContain('1 de 51');
    expect(text(harness)).toContain('MercadoPago · Approved');
    expect(text(harness)).toContain('ApprovedAfterExpiry');
    expect(text(harness)).toContain('Requiere revisión administrativa');
  });

  it('applies exact payment filters including manual review true/false/unset', async () => {
    const { component } = await navigate();
    httpMock.expectOne('/api/admin/payments?page=1&pageSize=50').flush({
      items: [],
      page: 1,
      pageSize: 50,
      totalCount: 0
    });

    component.setDraft('buildingId', 'building-1');
    component.setDraft('method', 'Cash');
    component.setDraft('status', 'Rejected');
    component.setManualReview('false');
    component.setDraft('reservationId', 'reservation-1');
    component.setDraft('fromUtc', '2026-10-01T00:00:00Z');
    component.setDraft('toUtc', '2026-10-02T00:00:00Z');
    component.setDraft('pageSize', 25);
    component.applyFilters(new Event('submit'));

    const filteredRequest = httpMock.expectOne((request) => request.url === '/api/admin/payments');
    expect(filteredRequest.request.params.get('buildingId')).toBe('building-1');
    expect(filteredRequest.request.params.get('method')).toBe('Cash');
    expect(filteredRequest.request.params.get('status')).toBe('Rejected');
    expect(filteredRequest.request.params.get('requiresManualReview')).toBe('false');
    expect(filteredRequest.request.params.get('reservationId')).toBe('reservation-1');
    expect(filteredRequest.request.params.get('fromUtc')).toBe('2026-10-01T00:00:00Z');
    expect(filteredRequest.request.params.get('toUtc')).toBe('2026-10-02T00:00:00Z');
    expect(filteredRequest.request.params.get('page')).toBe('1');
    expect(filteredRequest.request.params.get('pageSize')).toBe('25');
    filteredRequest.flush({ items: [], page: 1, pageSize: 25, totalCount: 0 });

    component.setManualReview('');
    component.applyFilters(new Event('submit'));
    const unsetRequest = httpMock.expectOne((request) => request.url === '/api/admin/payments');
    expect(unsetRequest.request.params.has('requiresManualReview')).toBe(false);
    expect(unsetRequest.request.params.get('buildingId')).toBe('building-1');
    expect(unsetRequest.request.params.get('method')).toBe('Cash');
    expect(unsetRequest.request.params.get('status')).toBe('Rejected');
    expect(unsetRequest.request.params.get('reservationId')).toBe('reservation-1');
    expect(unsetRequest.request.params.get('fromUtc')).toBe('2026-10-01T00:00:00Z');
    expect(unsetRequest.request.params.get('toUtc')).toBe('2026-10-02T00:00:00Z');
    expect(unsetRequest.request.params.get('page')).toBe('1');
    expect(unsetRequest.request.params.get('pageSize')).toBe('25');
    unsetRequest.flush({ items: [], page: 1, pageSize: 25, totalCount: 0 });
  });

  it('shows empty and error states', async () => {
    const { harness, component } = await navigate();
    httpMock.expectOne('/api/admin/payments?page=1&pageSize=50').flush({
      items: [],
      page: 1,
      pageSize: 50,
      totalCount: 0
    });
    await harness.fixture.whenStable();
    harness.detectChanges();

    expect(text(harness)).toContain('No hay pagos');

    component.reload();
    httpMock
      .expectOne('/api/admin/payments?page=1&pageSize=50')
      .flush({ status: 500, title: 'Backend detail' }, { status: 500, statusText: 'Error' });
    await harness.fixture.whenStable();
    harness.detectChanges();

    expect(text(harness)).toContain('Backend detail');
  });

  it('cancels stale requests when filters change quickly and never calls mutations', async () => {
    const { harness, component } = await navigate();
    const stale = httpMock.expectOne('/api/admin/payments?page=1&pageSize=50');

    component.setDraft('status', 'Pending');
    component.applyFilters(new Event('submit'));
    const fresh = httpMock.expectOne((request) => request.url === '/api/admin/payments');
    expect(fresh.request.params.get('status')).toBe('Pending');
    expect(fresh.request.params.get('page')).toBe('1');
    expect(fresh.request.params.get('pageSize')).toBe('50');

    expect(() => stale.flush({ items: [payment], page: 1, pageSize: 50, totalCount: 1 })).toThrow();

    fresh.flush({ items: [], page: 1, pageSize: 50, totalCount: 0 });
    await harness.fixture.whenStable();
    harness.detectChanges();

    expect(text(harness)).toContain('No hay pagos');
    expect(text(harness)).not.toContain('MercadoPago · Approved');
    httpMock.expectNone((request) => request.method !== 'GET');
  });
});
