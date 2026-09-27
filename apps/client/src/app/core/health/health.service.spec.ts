import { TestBed } from '@angular/core/testing';
import { firstValueFrom, of } from 'rxjs';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { ApiClient } from '../api/api-client.service';
import { HealthResponse, HealthService } from './health.service';

describe('HealthService', () => {
  const apiClient = {
    get: vi.fn()
  };

  let service: HealthService;

  beforeEach(() => {
    apiClient.get.mockReset();

    TestBed.configureTestingModule({
      providers: [
        HealthService,
        {
          provide: ApiClient,
          useValue: apiClient
        }
      ]
    });

    service = TestBed.inject(HealthService);
  });

  it('requests the API health endpoint and returns its response', async () => {
    const expected: HealthResponse = {
      status: 'ok',
      database: 'ok',
      utc: '2026-09-27T20:00:00Z'
    };

    apiClient.get.mockReturnValue(of(expected));

    const result = await firstValueFrom(service.check());

    expect(apiClient.get).toHaveBeenCalledExactlyOnceWith('/health');
    expect(result).toEqual(expected);
  });
});
