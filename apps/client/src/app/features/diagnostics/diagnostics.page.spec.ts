import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';

import { DiagnosticsPage } from './diagnostics.page';

describe('DiagnosticsPage', () => {
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()]
    });

    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('checks health against the root-level /health endpoint on init', async () => {
    const fixture = TestBed.createComponent(DiagnosticsPage);
    fixture.detectChanges();

    const request = httpMock.expectOne('/health');
    request.flush({ status: 'ok', database: 'ok', utc: '2026-09-27T20:00:00Z' });

    await fixture.whenStable();
    fixture.detectChanges();

    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('ok');
  });
});
