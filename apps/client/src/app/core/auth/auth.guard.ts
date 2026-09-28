import { inject } from '@angular/core';
import { CanMatchFn, Router, UrlSegment } from '@angular/router';

import { AuthService } from './auth.service';
import { AuthSessionStore } from './auth-session.store';
import { fallbackAuthenticatedUrl } from './return-url';

export const authenticatedGuard: CanMatchFn = async (_route, segments) => {
  const auth = inject(AuthService);
  const store = inject(AuthSessionStore);
  const router = inject(Router);

  await auth.initialize();

  if (store.isAuthenticated()) {
    return true;
  }

  return router.createUrlTree(['/login'], {
    queryParams: { returnUrl: toInternalUrl(segments) }
  });
};

export const anonymousGuard: CanMatchFn = async () => {
  const auth = inject(AuthService);
  const store = inject(AuthSessionStore);
  const router = inject(Router);

  await auth.initialize();

  return store.isAuthenticated() ? router.parseUrl(fallbackAuthenticatedUrl) : true;
};

function toInternalUrl(segments: UrlSegment[]): string {
  const path = `/${segments.map((segment) => segment.path).join('/')}`;
  return path === '/login' ? fallbackAuthenticatedUrl : path;
}

