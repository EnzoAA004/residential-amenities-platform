import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, throwError } from 'rxjs';

import { API_BASE_URL } from '../config/api-base-url.token';
import { AuthSessionStore } from './auth-session.store';
import { sanitizeReturnUrl } from './return-url';

const excludedAuthPaths = ['/auth/login', '/auth/me', '/auth/logout', '/auth/refresh'];

export const authInterceptor: HttpInterceptorFn = (request, next) => {
  const baseUrl = inject(API_BASE_URL).replace(/\/$/, '');
  const store = inject(AuthSessionStore);
  const router = inject(Router);

  return next(request).pipe(
    catchError((error: unknown) => {
      if (shouldHandleUnauthorized(error, request.url, baseUrl, router.url, store.isAuthenticated())) {
        store.setAnonymous();

        const returnUrl = sanitizeReturnUrl(router.url);
        void router.navigate(['/login'], {
          queryParams: returnUrl ? { returnUrl } : undefined
        });
      }

      return throwError(() => error);
    })
  );
};

function shouldHandleUnauthorized(
  error: unknown,
  requestUrl: string,
  baseUrl: string,
  currentUrl: string,
  isAuthenticated: boolean
): boolean {
  if (!(error instanceof HttpErrorResponse) || error.status !== 401) {
    return false;
  }

  if (isExcludedAuthEndpoint(requestUrl, baseUrl)) {
    return false;
  }

  if (!isAuthenticated || currentUrl.startsWith('/login')) {
    return false;
  }

  return true;
}

function isExcludedAuthEndpoint(requestUrl: string, baseUrl: string): boolean {
  const path = extractPath(requestUrl, baseUrl);
  return excludedAuthPaths.some((excludedPath) => path === excludedPath);
}

function extractPath(requestUrl: string, baseUrl: string): string {
  if (requestUrl.startsWith(baseUrl)) {
    return requestUrl.slice(baseUrl.length) || '/';
  }

  try {
    const parsed = new URL(requestUrl, window.location.origin);
    const normalizedBase = baseUrl.startsWith('/') ? baseUrl : new URL(baseUrl).pathname;

    return parsed.pathname.startsWith(normalizedBase)
      ? parsed.pathname.slice(normalizedBase.length) || '/'
      : parsed.pathname;
  } catch {
    return requestUrl;
  }
}

