import { HttpClient } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom, of } from 'rxjs';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { HealthResponse, HealthService } from './health.service';

describe('HealthService', () => {
  const httpClient = {
    get: vi.fn()
  };

  let service: HealthService;

  beforeEach(() => {
    httpClient.get.mockReset();

    TestBed.configureTestingModule({
      providers: [
        HealthService,
        {
          provide: HttpClient,
          useValue: httpClient
        }
      ]
    });

    service = TestBed.inject(HealthService);
  });

  it('requests the root-level /health endpoint directly, not through the /api catalog', async () => {
    const expected: HealthResponse = {
      status: 'ok',
      database: 'ok',
      utc: '2026-09-27T20:00:00Z'
    };

    httpClient.get.mockReturnValue(of(expected));

    const result = await firstValueFrom(service.check());

    expect(httpClient.get).toHaveBeenCalledExactlyOnceWith('/health');
    expect(result).toEqual(expected);
  });
});
