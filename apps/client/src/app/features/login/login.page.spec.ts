import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap } from '@angular/router';
import { Observable, Subject, of, throwError } from 'rxjs';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { ApiError } from '../../core/api/api-error';
import { AuthSessionStore } from '../../core/auth/auth-session.store';
import { AuthService } from '../../core/auth/auth.service';
import { CurrentUser, LoginCredentials } from '../../core/auth/auth.models';
import { LoginPage } from './login.page';

interface AuthServiceMock {
  login: ReturnType<typeof vi.fn>;
  loginDemo: ReturnType<typeof vi.fn>;
  demoStatus: ReturnType<typeof vi.fn>;
  logout: ReturnType<typeof vi.fn>;
}

describe('LoginPage', () => {
  let fixture: ComponentFixture<LoginPage>;
  let auth: AuthServiceMock;

  const currentUser: CurrentUser = {
    id: 'user-1',
    email: 'resident@example.test',
    displayName: 'Resident',
    roles: ['Resident'],
    memberships: []
  };

  beforeEach(async () => {
    auth = {
      login: vi.fn((_: LoginCredentials, __: string | null): Observable<CurrentUser> => of(currentUser)),
      loginDemo: vi.fn((_: string | null): Observable<CurrentUser> => of(currentUser)),
      demoStatus: vi.fn(() => of(true)),
      logout: vi.fn(() => of(undefined))
    };

    await TestBed.configureTestingModule({
      imports: [LoginPage],
      providers: [
        AuthSessionStore,
        { provide: AuthService, useValue: auth },
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: {
              queryParamMap: convertToParamMap({ returnUrl: '/reservations' })
            }
          }
        }
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(LoginPage);
    fixture.detectChanges();
  });

  it('renders labels and password field attributes', () => {
    const text = fixture.nativeElement.textContent as string;
    const inputs = fixture.nativeElement.querySelectorAll('ion-input');

    expect(text).toContain('Correo electrónico');
    expect(text).toContain('Contraseña');
    expect(inputs[0].getAttribute('type')).toBe('email');
    expect(inputs[0].getAttribute('autocomplete')).toBe('username');
    expect(inputs[1].getAttribute('type')).toBe('password');
    expect(inputs[1].getAttribute('autocomplete')).toBe('current-password');
  });

  it('shows the one-click live demo only when the backend enables it', () => {
    expect(fixture.nativeElement.textContent).toContain('Explorá la demo de residente');
    expect(fixture.nativeElement.textContent).toContain('Abrir demo');
  });

  it('starts the demo with the internal returnUrl', () => {
    fixture.componentInstance.startDemo();

    expect(auth.loginDemo).toHaveBeenCalledWith('/reservations');
  });

  it('does not submit an invalid form', () => {
    fixture.componentInstance.submit();

    expect(auth.login).not.toHaveBeenCalled();
    expect(fixture.componentInstance.email.touched).toBe(true);
    expect(fixture.componentInstance.password.touched).toBe(true);
  });

  it('submits credentials with the internal returnUrl', () => {
    fixture.componentInstance.form.setValue({
      email: 'resident@example.test',
      password: 'Test!Contraseña123'
    });

    fixture.componentInstance.submit();

    expect(auth.login).toHaveBeenCalledWith(
      {
        email: 'resident@example.test',
        password: 'Test!Contraseña123'
      },
      '/reservations'
    );
    expect(fixture.componentInstance.password.value).toBe('');
  });

  it('shows loading while sign-in is pending and avoids double submit', () => {
    const pending = new Subject<CurrentUser>();
    auth.login.mockReturnValue(pending.asObservable());
    fixture.componentInstance.form.setValue({
      email: 'resident@example.test',
      password: 'Test!Contraseña123'
    });

    fixture.componentInstance.submit();
    fixture.detectChanges();

    expect(fixture.componentInstance.loading()).toBe(true);
    expect(fixture.nativeElement.querySelector('ion-button')?.disabled).toBe(true);

    fixture.componentInstance.submit();
    expect(auth.login).toHaveBeenCalledTimes(1);

    pending.next(currentUser);
    pending.complete();
    expect(fixture.componentInstance.loading()).toBe(false);
  });

  it('shows a generic invalid-login error without user enumeration', () => {
    const error: ApiError = {
      status: 401,
      title: 'Authentication failed.'
    };
    auth.login.mockReturnValue(throwError(() => error));
    fixture.componentInstance.form.setValue({
      email: 'missing@example.test',
      password: 'wrong-password'
    });

    fixture.componentInstance.submit();
    fixture.detectChanges();

    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('No pudimos iniciar sesión con esas credenciales.');
    expect(text).not.toContain('email does not exist');
    expect(text).not.toContain('missing@example.test');
  });

  it('shows service-unavailable feedback without claiming credentials are wrong', () => {
    auth.login.mockReturnValue(
      throwError(() => ({
        status: 500,
        title: 'Something went wrong.'
      } satisfies ApiError))
    );
    fixture.componentInstance.form.setValue({
      email: 'resident@example.test',
      password: 'Test!Contraseña123'
    });

    fixture.componentInstance.submit();
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('El servicio de inicio de sesión no está disponible.');
  });
});

