import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { UrlSegment, provideRouter } from '@angular/router';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { adminGuard } from '../../core/auth/admin.guard';
import { AuthService } from '../../core/auth/auth.service';
import { AuthSessionStore } from '../../core/auth/auth-session.store';
import { API_BASE_URL } from '../../core/config/api-base-url.token';

describe('admin routing', () => {
  let httpMock: HttpTestingController;
  let store: AuthSessionStore;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: API_BASE_URL, useValue: '/api' },
        { provide: AuthService, useValue: { initialize: vi.fn().mockResolvedValue(undefined) } }
      ]
    });

    httpMock = TestBed.inject(HttpTestingController);
    store = TestBed.inject(AuthSessionStore);
  });

  afterEach(() => httpMock.verify());

  it('redirects Resident users away from /admin without calling admin APIs', async () => {
    store.setAuthenticated({
      id: 'resident',
      email: 'resident@example.test',
      displayName: 'Resident',
      roles: ['Resident'],
      memberships: []
    });

    const result = await TestBed.runInInjectionContext(() =>
      adminGuard(
        {} as never,
        [new UrlSegment('admin', {}), new UrlSegment('reservations', {})],
        {} as never
      )
    );

    expect(result.toString()).toBe('/');
    httpMock.expectNone((request) => request.url.startsWith('/api/admin'));
  });

  it('blocks a Resident session from /admin/audit before any HTTP request fires', async () => {
    store.setAuthenticated({
      id: 'resident',
      email: 'resident@example.test',
      displayName: 'Resident',
      roles: ['Resident'],
      memberships: []
    });

    const result = await TestBed.runInInjectionContext(() =>
      adminGuard(
        {} as never,
        [new UrlSegment('admin', {}), new UrlSegment('audit', {})],
        {} as never
      )
    );

    expect(result.toString()).toBe('/');
    httpMock.expectNone((request) => request.url.startsWith('/api/admin'));
    httpMock.expectNone((request) => request.url.includes('/audit'));
  });

  it('blocks a Resident session from /admin/payment-review before any HTTP request fires', async () => {
    store.setAuthenticated({
      id: 'resident',
      email: 'resident@example.test',
      displayName: 'Resident',
      roles: ['Resident'],
      memberships: []
    });

    const result = await TestBed.runInInjectionContext(() =>
      adminGuard(
        {} as never,
        [new UrlSegment('admin', {}), new UrlSegment('payment-review', {})],
        {} as never
      )
    );

    expect(result.toString()).toBe('/');
    httpMock.expectNone((request) => request.url.startsWith('/api/admin'));
    httpMock.expectNone((request) => request.url.includes('/payments'));
  });

  it('allows Administrator users into /admin', async () => {
    store.setAuthenticated({
      id: 'admin',
      email: 'admin@example.test',
      displayName: 'Admin',
      roles: ['Administrator'],
      memberships: []
    });

    const result = await TestBed.runInInjectionContext(() =>
      adminGuard({} as never, [new UrlSegment('admin', {})], {} as never)
    );

    expect(result).toBe(true);
  });
});
