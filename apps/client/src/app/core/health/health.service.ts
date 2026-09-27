import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { ApiClient } from '../api/api-client.service';

export type HealthResponse = {
  status: string;
  database: string;
  utc: string;
};

@Injectable({
  providedIn: 'root'
})
export class HealthService {
  private readonly api = inject(ApiClient);

  check(): Observable<HealthResponse> {
    return this.api.get<HealthResponse>('/health');
  }
}
