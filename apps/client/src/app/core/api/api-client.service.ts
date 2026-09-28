import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, catchError, throwError } from 'rxjs';

import { API_BASE_URL } from '../config/api-base-url.token';
import { toApiError } from './api-error';
import { ApiQueryParams, toHttpParams } from './api-query-params';

/**
 * Typed HTTP client for the backend's `/api` product catalog
 * (see {@link apiPaths}). Every call resolves `path` against
 * `API_BASE_URL` (same-origin `/api`) and rejects with a structured
 * {@link ApiError} instead of a raw `HttpErrorResponse` — features never
 * need to branch on `HttpErrorResponse` or `error.error` themselves.
 *
 * This is deliberately not for the backend's root-level `/health` endpoint,
 * which lives outside the `/api` catalog — see `core/health/health.service.ts`.
 */
@Injectable({
  providedIn: 'root'
})
export class ApiClient {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL).replace(/\/$/, '');

  get<TResponse>(path: string, params?: ApiQueryParams): Observable<TResponse> {
    return this.http
      .get<TResponse>(this.resolve(path), { params: toHttpParams(params) })
      .pipe(catchError(handleError));
  }

  post<TResponse, TBody = unknown>(
    path: string,
    body: TBody,
    params?: ApiQueryParams
  ): Observable<TResponse> {
    return this.http
      .post<TResponse>(this.resolve(path), body, { params: toHttpParams(params) })
      .pipe(catchError(handleError));
  }

  put<TResponse, TBody = unknown>(
    path: string,
    body: TBody,
    params?: ApiQueryParams
  ): Observable<TResponse> {
    return this.http
      .put<TResponse>(this.resolve(path), body, { params: toHttpParams(params) })
      .pipe(catchError(handleError));
  }

  delete<TResponse = void>(path: string, params?: ApiQueryParams): Observable<TResponse> {
    return this.http
      .delete<TResponse>(this.resolve(path), { params: toHttpParams(params) })
      .pipe(catchError(handleError));
  }

  private resolve(path: string): string {
    const normalizedPath = path.startsWith('/') ? path : `/${path}`;
    return `${this.baseUrl}${normalizedPath}`;
  }
}

function handleError(error: unknown): Observable<never> {
  return throwError(() => toApiError(error));
}
