import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';

import { API_BASE_URL } from '../config/api-base-url.token';
import { ApiClient } from './api-client.service';
import { ApiError } from './api-error';

describe('ApiClient', () => {
  let client: ApiClient;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        ApiClient,
        { provide: API_BASE_URL, useValue: '/api' }
      ]
    });

    client = TestBed.inject(ApiClient);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  interface Widget {
    id: string;
  }

  it('resolves a GET against the configured base URL', async () => {
    const result$ = client.get<Widget>('/widgets/1');

    const request = httpMock.expectOne('/api/widgets/1');
    expect(request.request.method).toBe('GET');
    request.flush({ id: '1' });

    await expect(firstValueFrom(result$)).resolves.toEqual({ id: '1' });
  });

  it('normalizes a path without a leading slash the same as one with it', () => {
    client.get<Widget>('widgets/1').subscribe();
    httpMock.expectOne('/api/widgets/1');

    client.get<Widget>('/widgets/1').subscribe();
    httpMock.expectOne('/api/widgets/1');
  });

  it('sends a POST with its body', async () => {
    const result$ = client.post<Widget, { name: string }>('/widgets', { name: 'sum' });

    const request = httpMock.expectOne('/api/widgets');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ name: 'sum' });
    request.flush({ id: '1' });

    await expect(firstValueFrom(result$)).resolves.toEqual({ id: '1' });
  });

  it('sends a PUT with its body', async () => {
    const result$ = client.put<Widget, { name: string }>('/widgets/1', { name: 'renamed' });

    const request = httpMock.expectOne('/api/widgets/1');
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({ name: 'renamed' });
    request.flush({ id: '1' });

    await expect(firstValueFrom(result$)).resolves.toEqual({ id: '1' });
  });

  it('sends a DELETE', async () => {
    const result$ = client.delete<void>('/widgets/1');

    const request = httpMock.expectOne('/api/widgets/1');
    expect(request.request.method).toBe('DELETE');
    request.flush(null);

    await expect(firstValueFrom(result$)).resolves.toBeNull();
  });

  it('appends query parameters, repeating array values and skipping null/undefined', () => {
    client
      .get<Widget[]>('/widgets', { buildingId: 'b1', tag: ['a', 'b'], page: 1, missing: undefined })
      .subscribe();

    const request = httpMock.expectOne(
      (req) => req.url === '/api/widgets' && req.params.get('buildingId') === 'b1'
    );

    expect(request.request.params.getAll('tag')).toEqual(['a', 'b']);
    expect(request.request.params.get('page')).toBe('1');
    expect(request.request.params.has('missing')).toBe(false);
    request.flush([]);
  });

  it.each([400, 401, 403, 404, 409, 422, 500])(
    'maps a %i ProblemDetails response to a structured ApiError',
    async (status) => {
      const result$ = client.get<Widget>('/widgets/1');
      const request = httpMock.expectOne('/api/widgets/1');

      request.flush(
        { status, title: 'Backend title', detail: 'Backend detail', type: 'about:blank' },
        { status, statusText: 'Error' }
      );

      const error = await result$.toPromise().catch((caught: ApiError) => caught);

      expect(error).toEqual({
        status,
        title: 'Backend title',
        detail: 'Backend detail',
        type: 'about:blank',
        instance: undefined
      });
    }
  );

  it('falls back to a safe generic error when the body is not ProblemDetails', async () => {
    const result$ = client.get<Widget>('/widgets/1');
    const request = httpMock.expectOne('/api/widgets/1');

    request.flush('<html>Internal Server Error</html>', {
      status: 500,
      statusText: 'Internal Server Error'
    });

    const error = await result$.toPromise().catch((caught: ApiError) => caught);

    expect(error).toEqual({ status: 500, title: 'Something went wrong. Please try again.' });
    // Never leaks the raw body into the error the UI would render.
    expect(JSON.stringify(error)).not.toContain('Internal Server Error');
  });

  it('maps a network failure (status 0) to a safe connectivity error, never the raw event', async () => {
    const result$ = client.get<Widget>('/widgets/1');
    const request = httpMock.expectOne('/api/widgets/1');

    request.error(new ProgressEvent('error'), { status: 0, statusText: 'Unknown Error' });

    const error = await result$.toPromise().catch((caught: ApiError) => caught);

    expect(error).toEqual({
      status: 0,
      title: 'Could not reach the server. Check your connection and try again.'
    });
  });

  it('never rejects with the raw HttpErrorResponse', async () => {
    const result$ = client.get<Widget>('/widgets/1');
    const request = httpMock.expectOne('/api/widgets/1');
    request.flush({ title: 'x' }, { status: 400, statusText: 'Bad Request' });

    const error = await result$.toPromise().catch((caught: unknown) => caught);

    expect(error).not.toBeInstanceOf(HttpErrorResponse);
  });
});
