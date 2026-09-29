import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';

import { API_BASE_URL } from '../../../core/config/api-base-url.token';
import { AuditItem } from '../admin.models';
import { AdminAuditPage } from './admin-audit.page';

const auditItem: AuditItem = {
  id: 'audit-1',
  occurredAtUtc: '2026-10-01T10:05:00Z',
  buildingId: 'building-1',
  actorType: 'User',
  actorUserId: 'admin-1',
  action: 'CashPaymentConfirmed',
  targetType: 'Payment',
  targetId: 'payment-1',
  correlationId: 'trace-1',
  metadata: { amount: 12000, currency: 'ARS' }
};

describe('AdminAuditPage', () => {
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([{ path: 'admin/audit', component: AdminAuditPage }]),
        { provide: API_BASE_URL, useValue: '/api' }
      ]
    });

    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  async function navigate() {
    const harness = await RouterTestingHarness.create();
    const component = await harness.navigateByUrl('/admin/audit', AdminAuditPage);
    return { harness, component };
  }

  function text(harness: RouterTestingHarness): string {
    return harness.routeNativeElement!.textContent!;
  }

  it('requests the default page and renders audit fields including metadata as-is', async () => {
    const { harness } = await navigate();
    httpMock.expectOne('/api/admin/audit?page=1&pageSize=50').flush({
      items: [auditItem],
      page: 1,
      pageSize: 50,
      totalCount: 1
    });
    await harness.fixture.whenStable();
    harness.detectChanges();

    expect(text(harness)).toContain('CashPaymentConfirmed');
    expect(text(harness)).toContain('admin-1');
    expect(text(harness)).toContain('payment-1');
    expect(text(harness)).toContain('trace-1');
    expect(text(harness)).toContain(JSON.stringify(auditItem.metadata, null, 2));
  });

  it('sends every filter exactly (buildingId, actorUserId, action, targetType, targetId, fromUtc, toUtc, page, pageSize)', async () => {
    const { component } = await navigate();
    httpMock.expectOne('/api/admin/audit?page=1&pageSize=50').flush({
      items: [],
      page: 1,
      pageSize: 50,
      totalCount: 0
    });

    component.setDraft('buildingId', 'building-1');
    component.setDraft('actorUserId', 'admin-1');
    component.setDraft('action', 'CashPaymentConfirmed');
    component.setDraft('targetType', 'Payment');
    component.setDraft('targetId', 'payment-1');
    component.setDraft('fromUtc', '2026-10-01T00:00:00Z');
    component.setDraft('toUtc', '2026-10-02T00:00:00Z');
    component.setDraft('pageSize', 25);
    component.applyFilters(new Event('submit'));

    const request = httpMock.expectOne((req) => req.url === '/api/admin/audit');
    expect(request.request.params.get('buildingId')).toBe('building-1');
    expect(request.request.params.get('actorUserId')).toBe('admin-1');
    expect(request.request.params.get('action')).toBe('CashPaymentConfirmed');
    expect(request.request.params.get('targetType')).toBe('Payment');
    expect(request.request.params.get('targetId')).toBe('payment-1');
    expect(request.request.params.get('fromUtc')).toBe('2026-10-01T00:00:00Z');
    expect(request.request.params.get('toUtc')).toBe('2026-10-02T00:00:00Z');
    expect(request.request.params.get('page')).toBe('1');
    expect(request.request.params.get('pageSize')).toBe('25');
    request.flush({ items: [], page: 1, pageSize: 25, totalCount: 0 });
  });

  it('paginates using the page returned by the backend', async () => {
    const { harness, component } = await navigate();
    httpMock.expectOne('/api/admin/audit?page=1&pageSize=50').flush({
      items: [auditItem],
      page: 1,
      pageSize: 50,
      totalCount: 51
    });
    await harness.fixture.whenStable();
    harness.detectChanges();

    component.nextPage();
    const request = httpMock.expectOne((req) => req.url === '/api/admin/audit');
    expect(request.request.params.get('page')).toBe('2');
    request.flush({ items: [], page: 2, pageSize: 50, totalCount: 51 });
  });

  it('shows the real backend detail for an invalid action filter', async () => {
    const { harness, component } = await navigate();
    httpMock.expectOne('/api/admin/audit?page=1&pageSize=50').flush({
      items: [],
      page: 1,
      pageSize: 50,
      totalCount: 0
    });

    component.setDraft('buildingId', undefined);
    component.applyFilters(new Event('submit'));
    httpMock
      .expectOne((req) => req.url === '/api/admin/audit')
      .flush(
        { status: 400, title: 'Unknown audit action.', detail: "'Bogus' is not a known audit action." },
        { status: 400, statusText: 'Bad Request' }
      );
    await harness.fixture.whenStable();
    harness.detectChanges();

    expect(text(harness)).toContain("'Bogus' is not a known audit action.");
  });

  it('discards a stale response when filters change quickly (race-safety)', async () => {
    const { harness, component } = await navigate();
    const stale = httpMock.expectOne('/api/admin/audit?page=1&pageSize=50');

    component.setDraft('actorUserId', 'admin-1');
    component.applyFilters(new Event('submit'));
    const fresh = httpMock.expectOne((req) => req.url === '/api/admin/audit');
    expect(fresh.request.params.get('actorUserId')).toBe('admin-1');

    expect(() => stale.flush({ items: [auditItem], page: 1, pageSize: 50, totalCount: 1 })).toThrow();

    fresh.flush({ items: [], page: 1, pageSize: 50, totalCount: 0 });
    await harness.fixture.whenStable();
    harness.detectChanges();

    expect(text(harness)).toContain('No hay eventos de auditoría');
    // 'CashPaymentConfirmed' alone isn't a safe negative check — it's also
    // one of the static action-filter <select> options, always rendered
    // regardless of results. Check for content unique to an actual rendered
    // row from the stale item instead (its targetId never appears in the
    // filter form).
    expect(text(harness)).not.toContain(auditItem.targetId!);
    httpMock.expectNone((request) => request.method !== 'GET');
  });
});
