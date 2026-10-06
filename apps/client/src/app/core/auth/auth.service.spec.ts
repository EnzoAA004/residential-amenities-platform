import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { API_BASE_URL } from '../config/api-base-url.token';
import { AuthSessionStore } from './auth-session.store';
import { AuthService } from './auth.service';

describe('AuthService', () => {
  let auth: AuthService;
  let store: AuthSessionStore;
  let httpMock: HttpTestingController;
  let router: Router;

  const userResponse = {
    id: 'user-1',
    email: 'resident@example.test',
    displayName: 'Resident One',
    roles: ['Resident'],
    memberships: [
      {
        buildingId: 'building-1',
        unitId: 'unit-1',
        unit: '3A',
        building: 'Pilot Building'
      }
    ]
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: API_BASE_URL, useValue: '/api' }
      ]
    });

    auth = TestBed.inject(AuthService);
    store = TestBed.inject(AuthSessionStore);
    httpMock = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
    vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);
    vi.spyOn(router, 'navigate').mockResolvedValue(true);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('initializes from /auth/me and authenticates the session', async () => {
    const promise = auth.initialize();

    const request = httpMock.expectOne('/api/auth/me');
    expect(request.request.method).toBe('GET');
    request.flush(userResponse);

    await promise;

    expect(store.state()).toEqual({
      status: 'authenticated',
      user: {
        id: 'user-1',
        email: 'resident@example.test',
        displayName: 'Resident One',
        roles: ['Resident'],
        memberships: [
          {
            buildingId: 'building-1',
            unitId: 'unit-1',
            unit: '3A',
            building: 'Pilot Building'
          }
        ]
      }
    });
    expect(store.isResident()).toBe(true);
    expect(store.isAdministrator()).toBe(false);
  });

  it('treats a 401 /auth/me response as anonymous', async () => {
    const promise = auth.initialize();

    httpMock.expectOne('/api/auth/me').flush(
      { status: 401, title: 'Unauthorized' },
      { status: 401, statusText: 'Unauthorized' }
    );

    await promise;

    expect(store.state()).toEqual({ status: 'anonymous' });
  });

  it('logs in with cookies, then loads /auth/me before authenticating', async () => {
    const promise = firstValueFrom(
      auth.login({ email: 'resident@example.test', password: 'Test!Password123' })
    );

    const login = httpMock.expectOne(
      (request) =>
        request.url === '/api/auth/login' &&
        request.params.get('useCookies') === 'true'
    );
    expect(login.request.method).toBe('POST');
    expect(login.request.body).toEqual({
      email: 'resident@example.test',
      password: 'Test!Password123'
    });
    login.flush(null);

    const me = httpMock.expectOne('/api/auth/me');
    me.flush(userResponse);

    await expect(promise).resolves.toEqual({
      id: 'user-1',
      email: 'resident@example.test',
      displayName: 'Resident One',
      roles: ['Resident'],
      memberships: userResponse.memberships
    });
    expect(store.isAuthenticated()).toBe(true);
    expect(router.navigateByUrl).toHaveBeenCalledWith('/');
  });

  it('reads demo availability from the public demo status endpoint', async () => {
    const promise = firstValueFrom(auth.demoStatus());

    const request = httpMock.expectOne('/api/demo/status');
    expect(request.request.method).toBe('GET');
    request.flush({ enabled: true });

    await expect(promise).resolves.toBe(true);
  });

  it('opens a demo cookie session and resolves the current resident', async () => {
    const promise = firstValueFrom(auth.loginDemo('/reservations'));

    const session = httpMock.expectOne('/api/demo/session');
    expect(session.request.method).toBe('POST');
    expect(session.request.body).toBeNull();
    session.flush(null, { status: 204, statusText: 'No Content' });

    const me = httpMock.expectOne('/api/auth/me');
    me.flush(userResponse);

    await expect(promise).resolves.toMatchObject({
      email: 'resident@example.test',
      roles: ['Resident']
    });
    expect(store.isAuthenticated()).toBe(true);
    expect(router.navigateByUrl).toHaveBeenCalledWith('/reservations');
  });

  it('does not call /auth/me after an invalid login', async () => {
    const promise = firstValueFrom(
      auth.login({ email: 'missing@example.test', password: 'wrong' })
    ).catch((error: unknown) => error);

    httpMock.expectOne('/api/auth/login?useCookies=true').flush(
      { status: 401, title: 'Authentication failed.' },
      { status: 401, statusText: 'Unauthorized' }
    );

    const error = await promise;

    expect(error).toMatchObject({ status: 401, title: 'Authentication failed.' });
    expect(store.state()).toEqual({ status: 'anonymous' });
    httpMock.expectNone('/api/auth/me');
  });

  it('logs out and clears the visible session', async () => {
    store.setAuthenticated({
      id: 'user-1',
      email: 'resident@example.test',
      displayName: 'Resident One',
      roles: ['Resident'],
      memberships: []
    });

    const promise = firstValueFrom(auth.logout());

    const request = httpMock.expectOne('/api/auth/logout');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toBeNull();
    request.flush(null, { status: 204, statusText: 'No Content' });

    await promise;

    expect(store.state()).toEqual({ status: 'anonymous' });
    expect(router.navigate).toHaveBeenCalledWith(['/login']);
  });

  it('treats an already-expired logout as anonymous', async () => {
    store.setAuthenticated({
      id: 'user-1',
      email: 'resident@example.test',
      displayName: 'Resident One',
      roles: ['Resident'],
      memberships: []
    });

    const promise = firstValueFrom(auth.logout());

    httpMock.expectOne('/api/auth/logout').flush(
      { status: 401, title: 'Unauthorized' },
      { status: 401, statusText: 'Unauthorized' }
    );

    await promise;

    expect(store.state()).toEqual({ status: 'anonymous' });
  });

  it('derives Administrator access as including Resident access for UX', () => {
    store.setAuthenticated({
      id: 'admin-1',
      email: 'admin@example.test',
      displayName: 'Admin One',
      roles: ['Administrator'],
      memberships: []
    });

    expect(store.isAdministrator()).toBe(true);
    expect(store.isResident()).toBe(true);
  });
});

