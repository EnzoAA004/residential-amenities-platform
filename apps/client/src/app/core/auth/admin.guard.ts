import { inject } from '@angular/core';
import { CanMatchFn, Router, UrlSegment } from '@angular/router';

import { AuthService } from './auth.service';
import { AuthSessionStore } from './auth-session.store';
import { fallbackAuthenticatedUrl } from './return-url';

export const adminGuard: CanMatchFn = async (_route, segments) => {
  const auth = inject(AuthService);
  const store = inject(AuthSessionStore);
  const router = inject(Router);

  await auth.initialize();

  if (!store.isAuthenticated()) {
    return router.createUrlTree(['/login'], {
      queryParams: { returnUrl: toInternalUrl(segments) }
    });
  }

  return store.isAdministrator() ? true : router.parseUrl(fallbackAuthenticatedUrl);
};

function toInternalUrl(segments: UrlSegment[]): string {
  const path = `/${segments.map((segment) => segment.path).join('/')}`;
  return path === '/login' ? fallbackAuthenticatedUrl : path;
}

