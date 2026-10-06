import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';

import { API_BASE_URL } from '../../../core/config/api-base-url.token';
import { AdminPriceRule } from '../admin.models';
import { AdminPricingPage } from './admin-pricing.page';

// Example values only — never real production pricing.
const rule: AdminPriceRule = {
  id: 'rule-1',
  buildingId: 'building-1',
  amenityId: 'amenity-1',
  componentType: 'Base',
  useType: 'SharedLeisure',
  currency: 'XTS',
  amount: 0.01,
  effectiveFromUtc: '2026-10-01T00:00:00Z',
  effectiveToUtc: null
};

describe('AdminPricingPage', () => {
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([{ path: 'admin/pricing', component: AdminPricingPage }]),
        { provide: API_BASE_URL, useValue: '/api' }
      ]
    });

    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  async function navigate() {
    const harness = await RouterTestingHarness.create();
    const component = await harness.navigateByUrl('/admin/pricing', AdminPricingPage);
    return { harness, component };
  }

  function text(harness: RouterTestingHarness): string {
    return harness.routeNativeElement!.textContent!;
  }

  it('does not query until a buildingId is provided, then lists and paginates', async () => {
    const { harness, component } = await navigate();
    httpMock.expectNone((request) => request.url.startsWith('/api/admin/pricing'));
    expect(text(harness)).toContain('Ingresá un ID de edificio');

    component.draftBuildingId.set('building-1');
    component.draftAmenityId.set('amenity-1');
    component.applyFilters(new Event('submit'));

    const request = httpMock.expectOne(
      (r) => r.url === '/api/admin/pricing/rules' && r.method === 'GET'
    );
    expect(request.request.params.get('buildingId')).toBe('building-1');
    expect(request.request.params.get('amenityId')).toBe('amenity-1');
    expect(request.request.params.get('page')).toBe('1');
    expect(request.request.params.get('pageSize')).toBe('50');
    request.flush({ items: [rule], page: 1, pageSize: 50, totalCount: 51 });
    await harness.fixture.whenStable();
    harness.detectChanges();

    expect(text(harness)).toContain('SharedLeisure · Base');
    expect(text(harness)).toContain('Página 1');

    component.nextPage();
    const page2 = httpMock.expectOne((r) => r.url === '/api/admin/pricing/rules');
    expect(page2.request.params.get('page')).toBe('2');
    page2.flush({ items: [], page: 2, pageSize: 50, totalCount: 51 });
  });

  it('creates a rule with no superseded rules and shows that explicitly', async () => {
    const { harness, component } = await navigate();

    component.setCreateField('buildingId', 'building-1');
    component.setCreateField('amenityId', 'amenity-1');
    component.setCreateField('componentType', 'Base');
    component.setCreateField('useType', 'SharedLeisure');
    component.setCreateField('currency', 'xts');
    component.setCreateField('amount', '0.01');
    component.submitCreate(new Event('submit'));

    const request = httpMock.expectOne('/api/admin/pricing/rules');
    expect(request.request.method).toBe('POST');
    expect(request.request.body.currency).toBe('XTS');
    expect(request.request.body.amount).toBe(0.01);
    request.flush({ created: rule, superseded: [] }, { status: 201, statusText: 'Created' });
    await harness.fixture.whenStable();
    harness.detectChanges();

    expect(text(harness)).toContain('Regla creada');
    expect(text(harness)).toContain('No se reemplazó ninguna regla existente.');
    expect(text(harness)).not.toContain('editada');
  });

  it('creates a rule that supersedes an existing one and shows both, never as an edit', async () => {
    const { harness, component } = await navigate();

    component.setCreateField('buildingId', 'building-1');
    component.setCreateField('amenityId', 'amenity-1');
    component.setCreateField('currency', 'XTS');
    component.setCreateField('amount', '0.02');
    component.submitCreate(new Event('submit'));

    const superseded: AdminPriceRule = { ...rule, id: 'rule-old', amount: 0.01, effectiveToUtc: '2026-10-01T00:00:00Z' };
    const created: AdminPriceRule = { ...rule, id: 'rule-new', amount: 0.02 };
    httpMock.expectOne('/api/admin/pricing/rules').flush(
      { created, superseded: [superseded] },
      { status: 201, statusText: 'Created' }
    );
    await harness.fixture.whenStable();
    harness.detectChanges();

    expect(text(harness)).toContain('Regla creada');
    expect(text(harness)).toContain('Reglas reemplazadas (superseded), no editadas');
  });

  it('shows the real detail for a 409 ambiguous rule', async () => {
    const { harness, component } = await navigate();

    component.setCreateField('buildingId', 'building-1');
    component.setCreateField('amenityId', 'amenity-1');
    component.setCreateField('currency', 'XTS');
    component.setCreateField('amount', '0.01');
    component.submitCreate(new Event('submit'));

    httpMock.expectOne('/api/admin/pricing/rules').flush(
      { status: 409, title: 'Unable to complete this operation.', detail: 'Another price rule overlaps this one.' },
      { status: 409, statusText: 'Conflict' }
    );
    await harness.fixture.whenStable();
    harness.detectChanges();

    expect(text(harness)).toContain('Another price rule overlaps this one.');
  });

  it('shows the real detail for a 422 backdated rule and never resubmits with a corrected date', async () => {
    const { harness, component } = await navigate();

    component.setCreateField('buildingId', 'building-1');
    component.setCreateField('amenityId', 'amenity-1');
    component.setCreateField('currency', 'XTS');
    component.setCreateField('amount', '0.01');
    component.setCreateField('effectiveFromUtc', '2020-01-01T00:00:00Z');
    component.submitCreate(new Event('submit'));

    httpMock.expectOne('/api/admin/pricing/rules').flush(
      { status: 422, title: 'Unable to complete this operation.', detail: 'A price rule cannot be backdated.' },
      { status: 422, statusText: 'Unprocessable Entity' }
    );
    await harness.fixture.whenStable();
    harness.detectChanges();

    expect(text(harness)).toContain('A price rule cannot be backdated.');
    httpMock.expectNone((r) => r.method === 'POST');
  });

  it('blocks client-side a negative amount, without calling the backend', async () => {
    const { component } = await navigate();

    component.setCreateField('buildingId', 'building-1');
    component.setCreateField('amenityId', 'amenity-1');
    component.setCreateField('currency', 'XTS');
    component.setCreateField('amount', '-1');
    component.submitCreate(new Event('submit'));

    httpMock.expectNone('/api/admin/pricing/rules');
  });

  it('allows a zero amount, per DEC-014 (free Leisure reservations)', async () => {
    const { component } = await navigate();

    component.setCreateField('buildingId', 'building-1');
    component.setCreateField('amenityId', 'amenity-1');
    component.setCreateField('currency', 'XTS');
    component.setCreateField('amount', '0');
    component.submitCreate(new Event('submit'));

    const request = httpMock.expectOne('/api/admin/pricing/rules');
    expect(request.request.body.amount).toBe(0);
    request.flush({ created: { ...rule, amount: 0 }, superseded: [] }, { status: 201, statusText: 'Created' });
  });

  it('blocks client-side a currency that is not exactly 3 letters, without calling the backend', async () => {
    const { component } = await navigate();

    component.setCreateField('buildingId', 'building-1');
    component.setCreateField('amenityId', 'amenity-1');
    component.setCreateField('currency', 'XT');
    component.setCreateField('amount', '0.01');
    component.submitCreate(new Event('submit'));

    httpMock.expectNone('/api/admin/pricing/rules');
  });
});
