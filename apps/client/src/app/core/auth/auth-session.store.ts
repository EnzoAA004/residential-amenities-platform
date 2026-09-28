import { Injectable, computed, signal } from '@angular/core';

import { AuthState, CurrentUser, authRoles } from './auth.models';

@Injectable({
  providedIn: 'root'
})
export class AuthSessionStore {
  private readonly stateSignal = signal<AuthState>({ status: 'checking' });

  readonly state = this.stateSignal.asReadonly();
  readonly isChecking = computed(() => this.state().status === 'checking');
  readonly isAuthenticated = computed(() => this.state().status === 'authenticated');
  readonly currentUser = computed(() => {
    const state = this.state();
    return state.status === 'authenticated' ? state.user : null;
  });
  readonly roles = computed(() => this.currentUser()?.roles ?? []);
  readonly memberships = computed(() => this.currentUser()?.memberships ?? []);
  readonly isAdministrator = computed(() =>
    this.roles().includes(authRoles.administrator)
  );
  readonly isResident = computed(() =>
    this.roles().includes(authRoles.resident) || this.isAdministrator()
  );

  setChecking(): void {
    this.stateSignal.set({ status: 'checking' });
  }

  setAuthenticated(user: CurrentUser): void {
    this.stateSignal.set({ status: 'authenticated', user });
  }

  setAnonymous(): void {
    this.stateSignal.set({ status: 'anonymous' });
  }
}

