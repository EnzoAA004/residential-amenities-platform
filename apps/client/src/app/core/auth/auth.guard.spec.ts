import { TestBed } from '@angular/core/testing';
import { UrlSegment, provideRouter } from '@angular/router';
import { describe, expect, it, vi } from 'vitest';

import { AuthSessionStore } from './auth-session.store';
import { AuthService } from './auth.service';
import { adminGuard } from './admin.guard';
import { authenticatedGuard } from './auth.guard';

describe('auth route guards', () => {
  function setup() {
    const initialize = vi.fn<() => Promise<void>>().mockResolvedValue(undefined);

    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: AuthService, useValue: { initialize } }
      ]
    });

    return {
      initialize,
      store: TestBed.inject(AuthSessionStore)
    };
  }

  it('redirects anonymous users to /login for authenticated routes', async () => {
    const { store } = setup();
    store.setAnonymous();

    const result = await TestBed.runInInjectionContext(() =>
      authenticatedGuard({} as never, [new UrlSegment('reservations', {})], {} as never)
    );

    expect(result.toString()).toBe('/login?returnUrl=%2Freservations');
  });

  it('allows authenticated Resident users through authenticated routes', async () => {
    const { store } = setup();
    store.setAuthenticated({
      id: 'resident',
      email: 'resident@example.test',
      displayName: 'Resident',
      roles: ['Resident'],
      memberships: []
    });

    const result = await TestBed.runInInjectionContext(() =>
      authenticatedGuard({} as never, [], {} as never)
    );

    expect(result).toBe(true);
  });

  it('allows authenticated Administrator users through authenticated routes', async () => {
    const { store } = setup();
    store.setAuthenticated({
      id: 'admin',
      email: 'admin@example.test',
      displayName: 'Admin',
      roles: ['Administrator'],
      memberships: []
    });

    const result = await TestBed.runInInjectionContext(() =>
      authenticatedGuard({} as never, [], {} as never)
    );

    expect(result).toBe(true);
  });

  it('waits for initialization before deciding while checking', async () => {
    const { initialize, store } = setup();
    store.setChecking();

    const result = await TestBed.runInInjectionContext(() =>
      authenticatedGuard({} as never, [], {} as never)
    );

    expect(initialize).toHaveBeenCalledOnce();
    expect(result.toString()).toBe('/login?returnUrl=%2F');
  });

  it('redirects anonymous users to /login for admin routes', async () => {
    const { store } = setup();
    store.setAnonymous();

    const result = await TestBed.runInInjectionContext(() =>
      adminGuard(
        {} as never,
        [new UrlSegment('admin', {}), new UrlSegment('settings', {})],
        {} as never
      )
    );

    expect(result.toString()).toBe('/login?returnUrl=%2Fadmin%2Fsettings');
  });

  it('redirects Resident users away from admin routes', async () => {
    const { store } = setup();
    store.setAuthenticated({
      id: 'resident',
      email: 'resident@example.test',
      displayName: 'Resident',
      roles: ['Resident'],
      memberships: []
    });

    const result = await TestBed.runInInjectionContext(() =>
      adminGuard({} as never, [new UrlSegment('admin', {})], {} as never)
    );

    expect(result.toString()).toBe('/');
  });

  it('allows Administrator users through admin routes', async () => {
    const { store } = setup();
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

