import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

export interface HealthResponse {
  status: string;
  database: string;
  utc: string;
}

/**
 * Internal operational diagnostic, not part of the product UI.
 *
 * Calls the backend's root-level `/health` endpoint directly with
 * `HttpClient`, deliberately bypassing `ApiClient`/`API_BASE_URL`: `/health`
 * is an operations endpoint outside the `/api` product catalog (see
 * `apiPaths`), not a backend feature this client's error/path conventions
 * are meant to wrap. The dev proxy (`proxy.conf.json`) forwards `/health`
 * to the backend the same way it forwards `/api`.
 */
@Injectable({
  providedIn: 'root'
})
export class HealthService {
  private readonly http = inject(HttpClient);

  check(): Observable<HealthResponse> {
    return this.http.get<HealthResponse>('/health');
  }
}
