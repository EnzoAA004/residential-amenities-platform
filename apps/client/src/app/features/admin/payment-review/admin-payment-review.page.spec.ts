import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';

import { API_BASE_URL } from '../../../core/config/api-base-url.token';
import { AdminPayment } from '../admin.models';
import { AdminPaymentReviewPage } from './admin-payment-review.page';

function payment(overrides: Partial<AdminPayment>): AdminPayment {
  return {
    paymentId: 'payment-1',
    reservationId: 'reservation-1',
    buildingId: 'building-1',
    method: 'MercadoPago',
    status: 'Approved',
    amount: 12000,
    currency: 'ARS',
    createdAtUtc: '2026-10-01T10:00:00Z',
    approvedAtUtc: '2026-10-01T10:05:00Z',
    reservationOutcome: 'ApprovedAfterExpiry',
    requiresManualReview: true,
    providerStatus: 'approved',
    providerStatusDetail: null,
    cashDeclaredAtUtc: null,
    cashConfirmedAtUtc: null,
    cashConfirmedByUserId: null,
    ...overrides
  };
}

describe('AdminPaymentReviewPage', () => {
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([{ path: 'admin/payment-review', component: AdminPaymentReviewPage }]),
        { provide: API_BASE_URL, useValue: '/api' }
      ]
    });

    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  async function navigate() {
    const harness = await RouterTestingHarness.create();
    const component = await harness.navigateByUrl('/admin/payment-review', AdminPaymentReviewPage);
    return { harness, component };
  }

  function text(harness: RouterTestingHarness): string {
    return harness.routeNativeElement!.textContent!;
  }

  it('always sends requiresManualReview=true against the existing admin/payments endpoint', async () => {
    await navigate();
    const request = httpMock.expectOne(
      (req) => req.url === '/api/admin/payments'
    );
    expect(request.request.params.get('requiresManualReview')).toBe('true');
    expect(request.request.params.get('page')).toBe('1');
    expect(request.request.params.get('pageSize')).toBe('50');
    request.flush({ items: [], page: 1, pageSize: 50, totalCount: 0 });
  });

  it('includes optional filters alongside the fixed requiresManualReview filter', async () => {
    const { component } = await navigate();
    httpMock.expectOne((req) => req.url === '/api/admin/payments').flush({
      items: [],
      page: 1,
      pageSize: 50,
      totalCount: 0
    });

    component.setDraft('buildingId', 'building-1');
    component.setDraft('reservationId', 'reservation-1');
    component.setDraft('fromUtc', '2026-10-01T00:00:00Z');
    component.setDraft('toUtc', '2026-10-02T00:00:00Z');
    component.applyFilters(new Event('submit'));

    const request = httpMock.expectOne((req) => req.url === '/api/admin/payments');
    expect(request.request.params.get('requiresManualReview')).toBe('true');
    expect(request.request.params.get('buildingId')).toBe('building-1');
    expect(request.request.params.get('reservationId')).toBe('reservation-1');
    expect(request.request.params.get('fromUtc')).toBe('2026-10-01T00:00:00Z');
    expect(request.request.params.get('toUtc')).toBe('2026-10-02T00:00:00Z');
    request.flush({ items: [], page: 1, pageSize: 50, totalCount: 0 });
  });

  it.each([
    ['ApprovedAfterExpiry', 'después de que venciera el plazo'],
    ['ApprovedForCancelledReservation', 'ya estaba cancelada'],
    ['ApprovedForMissingReservation', 'no se encontró la reserva asociada']
  ])('renders the factual explanation for outcome %s', async (outcome, expectedSnippet) => {
    const { harness } = await navigate();
    httpMock.expectOne((req) => req.url === '/api/admin/payments').flush({
      items: [payment({ reservationOutcome: outcome })],
      page: 1,
      pageSize: 50,
      totalCount: 1
    });
    await harness.fixture.whenStable();
    harness.detectChanges();

    expect(text(harness)).toContain(outcome);
    expect(text(harness)).toContain(expectedSnippet);
  });

  it('never renders a resolve/approve/refund/dismiss action', async () => {
    const { harness } = await navigate();
    httpMock.expectOne((req) => req.url === '/api/admin/payments').flush({
      items: [
        payment({ paymentId: 'payment-1', method: 'Cash', status: 'Pending', reservationOutcome: 'None' }),
        payment({ paymentId: 'payment-2', reservationOutcome: 'ApprovedForCancelledReservation' })
      ],
      page: 1,
      pageSize: 50,
      totalCount: 2
    });
    await harness.fixture.whenStable();
    harness.detectChanges();

    // Checked against actual controls, not the whole page's text: the
    // page's own read-only-notice copy legitimately explains that no
    // resolve/approve/refund action exists, which necessarily uses those
    // words in prose. What must never exist is a BUTTON offering one.
    const buttonLabels = Array.from(
      harness.routeNativeElement!.querySelectorAll('ion-button')
    ).map((button) => button.textContent!.trim().toLowerCase());

    for (const forbidden of ['resolver', 'aprobar', 'reembols', 'descartar', 'confirmar efectivo']) {
      expect(buttonLabels.some((label) => label.includes(forbidden))).toBe(false);
    }
    httpMock.expectNone((request) => request.method !== 'GET');
  });

  it('paginates using the page returned by the backend', async () => {
    const { harness, component } = await navigate();
    httpMock.expectOne((req) => req.url === '/api/admin/payments').flush({
      items: [payment({})],
      page: 1,
      pageSize: 50,
      totalCount: 51
    });
    await harness.fixture.whenStable();
    harness.detectChanges();

    component.nextPage();
    const request = httpMock.expectOne((req) => req.url === '/api/admin/payments');
    expect(request.request.params.get('page')).toBe('2');
    expect(request.request.params.get('requiresManualReview')).toBe('true');
    request.flush({ items: [], page: 2, pageSize: 50, totalCount: 51 });
  });
});
