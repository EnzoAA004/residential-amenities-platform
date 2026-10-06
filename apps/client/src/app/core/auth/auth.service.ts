import { Injectable, inject } from '@angular/core';
import { Router } from '@angular/router';
import { Observable, catchError, firstValueFrom, map, of, switchMap, tap, throwError } from 'rxjs';

import { ApiClient } from '../api/api-client.service';
import { ApiError } from '../api/api-error';
import { apiPaths } from '../api/api-paths';
import { AuthSessionStore } from './auth-session.store';
import { AuthRole, CurrentUser, LoginCredentials, ResidentMembershipContext } from './auth.models';
import { fallbackAuthenticatedUrl, sanitizeReturnUrl } from './return-url';

interface CurrentUserResponse {
  id: string;
  email: string;
  displayName: string;
  roles: string[];
  memberships: ResidentMembershipContext[];
}

interface DemoStatusResponse {
  enabled: boolean;
}

@Injectable({
  providedIn: 'root'
})
export class AuthService {
  private readonly api = inject(ApiClient);
  private readonly router = inject(Router);
  private readonly store = inject(AuthSessionStore);
  private initializePromise: Promise<void> | null = null;

  initialize(): Promise<void> {
    if (this.initializePromise) {
      return this.initializePromise;
    }

    this.store.setChecking();

    this.initializePromise = firstValueFrom(
      this.loadCurrentUser().pipe(
        tap((user) => this.store.setAuthenticated(user)),
        catchError((error: ApiError) => {
          this.store.setAnonymous();

          if (error.status === 401) {
            return of(null);
          }

          return of(null);
        }),
        map(() => undefined)
      )
    );

    return this.initializePromise;
  }

  login(credentials: LoginCredentials, returnUrl?: string | null): Observable<CurrentUser> {
    return this.api
      .post<void, LoginCredentials>(apiPaths.auth.login, credentials, { useCookies: true })
      .pipe(
        switchMap(() => this.loadCurrentUser()),
        tap((user) => this.store.setAuthenticated(user)),
        tap((user) => {
          void this.router.navigateByUrl(this.resolvePostLoginUrl(user, returnUrl));
        }),
        catchError((error: ApiError) => {
          this.store.setAnonymous();
          return throwError(() => error);
        })
      );
  }

  demoStatus(): Observable<boolean> {
    return this.api
      .get<DemoStatusResponse>(apiPaths.demo.status)
      .pipe(map((response) => response.enabled));
  }

  loginDemo(returnUrl?: string | null): Observable<CurrentUser> {
    return this.api
      .post<void, null>(apiPaths.demo.session, null)
      .pipe(
        switchMap(() => this.loadCurrentUser()),
        tap((user) => this.store.setAuthenticated(user)),
        tap((user) => {
          void this.router.navigateByUrl(this.resolvePostLoginUrl(user, returnUrl));
        }),
        catchError((error: ApiError) => {
          this.store.setAnonymous();
          return throwError(() => error);
        })
      );
  }

  logout(): Observable<void> {
    return this.api.post<void, null>(apiPaths.auth.logout, null).pipe(
      catchError((error: ApiError) => {
        if (error.status === 401) {
          return of(undefined);
        }

        return throwError(() => error);
      }),
      tap(() => {
        this.store.setAnonymous();
        void this.router.navigate(['/login']);
      }),
      map(() => undefined)
    );
  }

  loadCurrentUser(): Observable<CurrentUser> {
    return this.api
      .get<CurrentUserResponse>(apiPaths.auth.me)
      .pipe(map((response) => toCurrentUser(response)));
  }

  private resolvePostLoginUrl(user: CurrentUser, returnUrl?: string | null): string {
    const sanitized = sanitizeReturnUrl(returnUrl);

    if (sanitized && canUseReturnUrl(user, sanitized)) {
      return sanitized;
    }

    return fallbackAuthenticatedUrl;
  }
}

function toCurrentUser(response: CurrentUserResponse): CurrentUser {
  return {
    id: response.id,
    email: response.email,
    displayName: response.displayName,
    roles: response.roles.filter(isAuthRole),
    memberships: response.memberships.map((membership) => ({
      buildingId: membership.buildingId,
      unitId: membership.unitId,
      unit: membership.unit,
      building: membership.building
    }))
  };
}

function isAuthRole(role: string): role is AuthRole {
  return role === 'Resident' || role === 'Administrator';
}

function canUseReturnUrl(user: CurrentUser, returnUrl: string): boolean {
  if (!returnUrl.startsWith('/admin')) {
    return true;
  }

  return user.roles.includes('Administrator');
}

