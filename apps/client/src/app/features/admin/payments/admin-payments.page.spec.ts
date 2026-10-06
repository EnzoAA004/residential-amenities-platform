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

const pendingCashPayment: AdminPayment = {
  ...payment,
  paymentId: 'payment-cash-1',
  method: 'Cash',
  status: 'Pending',
  reservationOutcome: 'None',
  requiresManualReview: false,
  providerStatus: null,
  providerStatusDetail: null,
  cashDeclaredAtUtc: '2026-10-01T10:00:00Z'
};

const approvedCashPayment: AdminPayment = {
  ...pendingCashPayment,
  paymentId: 'payment-cash-2',
  status: 'Approved',
  cashConfirmedAtUtc: '2026-10-01T11:00:00Z',
  cashConfirmedByUserId: 'admin-1'
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

  describe('cash confirmation', () => {
    it('shows the confirm action only for Cash + Pending payments', async () => {
      const { harness } = await navigate();
      httpMock.expectOne('/api/admin/payments?page=1&pageSize=50').flush({
        items: [payment, pendingCashPayment, approvedCashPayment],
        page: 1,
        pageSize: 50,
        totalCount: 3
      });
      await harness.fixture.whenStable();
      harness.detectChanges();

      expect(text(harness)).toContain('Confirmar efectivo');
    });

    it('does not show the confirm action for a MercadoPago payment', async () => {
      const { harness } = await navigate();
      httpMock.expectOne('/api/admin/payments?page=1&pageSize=50').flush({
        items: [payment],
        page: 1,
        pageSize: 50,
        totalCount: 1
      });
      await harness.fixture.whenStable();
      harness.detectChanges();

      expect(text(harness)).not.toContain('Confirmar efectivo');
    });

    it('does not show the confirm action for an already-Approved Cash payment', async () => {
      const { harness } = await navigate();
      httpMock.expectOne('/api/admin/payments?page=1&pageSize=50').flush({
        items: [approvedCashPayment],
        page: 1,
        pageSize: 50,
        totalCount: 1
      });
      await harness.fixture.whenStable();
      harness.detectChanges();

      expect(text(harness)).not.toContain('Confirmar efectivo');
    });

    it('requires explicit confirmation before the POST fires', async () => {
      const { harness, component } = await navigate();
      httpMock.expectOne('/api/admin/payments?page=1&pageSize=50').flush({
        items: [pendingCashPayment],
        page: 1,
        pageSize: 50,
        totalCount: 1
      });
      await harness.fixture.whenStable();
      harness.detectChanges();

      component.armConfirmCash(pendingCashPayment.paymentId);
      harness.detectChanges();
      httpMock.expectNone((request) => request.method === 'POST');
      expect(text(harness)).toContain('¿Confirmar que se recibió el efectivo de este pago?');

      component.confirmCash(pendingCashPayment.paymentId);
      const request = httpMock.expectOne(
        `/api/payments/${pendingCashPayment.paymentId}/cash/confirm`
      );
      expect(request.request.method).toBe('POST');
      expect(request.request.body).toBeNull();
      request.flush({
        paymentId: pendingCashPayment.paymentId,
        reservationId: pendingCashPayment.reservationId,
        status: 'Approved',
        amount: pendingCashPayment.amount,
        currency: pendingCashPayment.currency,
        cashConfirmedAtUtc: '2026-10-01T12:00:00Z',
        cashConfirmedByUserId: 'admin-1',
        reservationOutcome: 'ReservationConfirmed',
        requiresManualReview: false
      });
    });

    it('cancelling the armed confirmation never fires the POST', async () => {
      const { harness, component } = await navigate();
      httpMock.expectOne('/api/admin/payments?page=1&pageSize=50').flush({
        items: [pendingCashPayment],
        page: 1,
        pageSize: 50,
        totalCount: 1
      });
      await harness.fixture.whenStable();

      component.armConfirmCash(pendingCashPayment.paymentId);
      component.cancelConfirmCash();
      harness.detectChanges();

      expect(text(harness)).not.toContain('¿Confirmar que se recibió el efectivo de este pago?');
      httpMock.expectNone((request) => request.method === 'POST');
    });

    it('updates the rendered outcome and requiresManualReview from the real response, never assuming Approved implies ReservationConfirmed', async () => {
      const { harness, component } = await navigate();
      httpMock.expectOne('/api/admin/payments?page=1&pageSize=50').flush({
        items: [pendingCashPayment],
        page: 1,
        pageSize: 50,
        totalCount: 1
      });
      await harness.fixture.whenStable();
      harness.detectChanges();

      component.confirmCash(pendingCashPayment.paymentId);
      httpMock.expectOne(`/api/payments/${pendingCashPayment.paymentId}/cash/confirm`).flush({
        paymentId: pendingCashPayment.paymentId,
        reservationId: pendingCashPayment.reservationId,
        status: 'Approved',
        amount: pendingCashPayment.amount,
        currency: pendingCashPayment.currency,
        cashConfirmedAtUtc: '2026-10-01T12:00:00Z',
        cashConfirmedByUserId: 'admin-1',
        reservationOutcome: 'ApprovedAfterExpiry',
        requiresManualReview: true
      });
      await harness.fixture.whenStable();
      harness.detectChanges();

      expect(text(harness)).toContain('Resultado: ApprovedAfterExpiry');
      expect(text(harness)).toContain('Requiere revisión administrativa');
      expect(text(harness)).not.toContain('Confirmar efectivo');
    });

    it('shows the real detail on a 409 (not a confirmable Cash/Pending payment)', async () => {
      const { harness, component } = await navigate();
      httpMock.expectOne('/api/admin/payments?page=1&pageSize=50').flush({
        items: [pendingCashPayment],
        page: 1,
        pageSize: 50,
        totalCount: 1
      });
      await harness.fixture.whenStable();
      harness.detectChanges();

      component.confirmCash(pendingCashPayment.paymentId);
      httpMock.expectOne(`/api/payments/${pendingCashPayment.paymentId}/cash/confirm`).flush(
        {
          status: 409,
          title: 'Unable to confirm this cash payment.',
          detail: 'This payment is not a Cash payment awaiting confirmation.'
        },
        { status: 409, statusText: 'Conflict' }
      );
      await harness.fixture.whenStable();
      harness.detectChanges();

      expect(text(harness)).toContain('This payment is not a Cash payment awaiting confirmation.');
    });

    it('shows the real detail on a 403', async () => {
      const { harness, component } = await navigate();
      httpMock.expectOne('/api/admin/payments?page=1&pageSize=50').flush({
        items: [pendingCashPayment],
        page: 1,
        pageSize: 50,
        totalCount: 1
      });
      await harness.fixture.whenStable();
      harness.detectChanges();

      component.confirmCash(pendingCashPayment.paymentId);
      httpMock.expectOne(`/api/payments/${pendingCashPayment.paymentId}/cash/confirm`).flush(
        { status: 403, title: 'Forbidden', detail: 'Only an Administrator can confirm cash.' },
        { status: 403, statusText: 'Forbidden' }
      );
      await harness.fixture.whenStable();
      harness.detectChanges();

      expect(text(harness)).toContain('Only an Administrator can confirm cash.');
    });

    it('blocks a double submit while a confirmation is already in flight', async () => {
      const { harness, component } = await navigate();
      httpMock.expectOne('/api/admin/payments?page=1&pageSize=50').flush({
        items: [pendingCashPayment],
        page: 1,
        pageSize: 50,
        totalCount: 1
      });
      await harness.fixture.whenStable();
      harness.detectChanges();

      component.confirmCash(pendingCashPayment.paymentId);
      component.confirmCash(pendingCashPayment.paymentId);

      httpMock.expectOne(`/api/payments/${pendingCashPayment.paymentId}/cash/confirm`).flush({
        paymentId: pendingCashPayment.paymentId,
        reservationId: pendingCashPayment.reservationId,
        status: 'Approved',
        amount: pendingCashPayment.amount,
        currency: pendingCashPayment.currency,
        cashConfirmedAtUtc: '2026-10-01T12:00:00Z',
        cashConfirmedByUserId: 'admin-1',
        reservationOutcome: 'ReservationConfirmed',
        requiresManualReview: false
      });
    });
  });
});
