import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { API_BASE_URL } from '../config/api-base-url.token';
import { AuthSessionStore } from './auth-session.store';
import { authInterceptor } from './auth.interceptor';

describe('authInterceptor', () => {
  let http: HttpClient;
  let httpMock: HttpTestingController;
  let store: AuthSessionStore;
  let router: Router;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: API_BASE_URL, useValue: '/api' }
      ]
    });

    http = TestBed.inject(HttpClient);
    httpMock = TestBed.inject(HttpTestingController);
    store = TestBed.inject(AuthSessionStore);
    router = TestBed.inject(Router);
    vi.spyOn(router, 'navigate').mockResolvedValue(true);
  });

  afterEach(() => {
    httpMock.verify();
  });

  function authenticate(): void {
    store.setAuthenticated({
      id: 'user-1',
      email: 'resident@example.test',
      displayName: 'Resident',
      roles: ['Resident'],
      memberships: []
    });
  }

  it('clears session and redirects on a protected 401', async () => {
    authenticate();

    const promise = firstValueFrom(http.get('/api/protected')).catch((error: unknown) => error);

    httpMock.expectOne('/api/protected').flush(
      { status: 401, title: 'Unauthorized' },
      { status: 401, statusText: 'Unauthorized' }
    );

    await promise;

    expect(store.state()).toEqual({ status: 'anonymous' });
    expect(router.navigate).toHaveBeenCalledWith(['/login'], {
      queryParams: { returnUrl: '/' }
    });
  });

  it('does not redirect or clear session for a login 401', async () => {
    authenticate();

    const promise = firstValueFrom(http.post('/api/auth/login', null)).catch((error: unknown) => error);

    httpMock.expectOne('/api/auth/login').flush(
      { status: 401, title: 'Authentication failed.' },
      { status: 401, statusText: 'Unauthorized' }
    );

    await promise;

    expect(store.isAuthenticated()).toBe(true);
    expect(router.navigate).not.toHaveBeenCalled();
  });

  it('does not clear session on 403', async () => {
    authenticate();

    const promise = firstValueFrom(http.get('/api/admin/anything')).catch((error: unknown) => error);

    httpMock.expectOne('/api/admin/anything').flush(
      { status: 403, title: 'Forbidden' },
      { status: 403, statusText: 'Forbidden' }
    );

    await promise;

    expect(store.isAuthenticated()).toBe(true);
    expect(router.navigate).not.toHaveBeenCalled();
  });

  it('does not clear session on 500', async () => {
    authenticate();

    const promise = firstValueFrom(http.get('/api/protected')).catch((error: unknown) => error);

    httpMock.expectOne('/api/protected').flush(
      { status: 500, title: 'Server error' },
      { status: 500, statusText: 'Server Error' }
    );

    await promise;

    expect(store.isAuthenticated()).toBe(true);
    expect(router.navigate).not.toHaveBeenCalled();
  });

  it('does not repeatedly navigate when already anonymous', async () => {
    store.setAnonymous();

    const promise = firstValueFrom(http.get('/api/protected')).catch((error: unknown) => error);

    httpMock.expectOne('/api/protected').flush(
      { status: 401, title: 'Unauthorized' },
      { status: 401, statusText: 'Unauthorized' }
    );

    await promise;

    expect(router.navigate).not.toHaveBeenCalled();
  });
});

