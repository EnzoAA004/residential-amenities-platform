import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { describe, expect, it } from 'vitest';

import { AuthSessionStore } from '../../core/auth/auth-session.store';
import { AuthService } from '../../core/auth/auth.service';
import { LandingPage } from './landing.page';

describe('LandingPage', () => {
  it('renders the product shell as the real landing, not the old health screen', async () => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        AuthSessionStore,
        { provide: AuthService, useValue: { logout: () => of(undefined) } }
      ]
    });

    const store = TestBed.inject(AuthSessionStore);
    store.setAuthenticated({
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
    });

    const fixture = TestBed.createComponent(LandingPage);
    fixture.detectChanges();
    await fixture.whenStable();

    const text = fixture.nativeElement.textContent as string;

    expect(text).toContain('Bienvenido, Resident One');
    expect(text).toContain('Active memberships: Pilot Building 3A');
    expect(text).toContain('Cerrar sesión');
    expect(text).toContain('Ver amenities');
    expect(text).not.toContain('Check API');
    expect(text).not.toContain('not checked');
  });
});
