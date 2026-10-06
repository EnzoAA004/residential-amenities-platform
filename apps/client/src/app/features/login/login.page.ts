import { Component, OnInit, inject, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import {
  IonButton,
  IonInput,
  IonItem,
  IonLabel,
  IonNote,
  IonSpinner,
  IonText
} from '@ionic/angular';

import { ApiError } from '../../core/api/api-error';
import { AuthService } from '../../core/auth/auth.service';
import { AppShellComponent } from '../../layout/app-shell/app-shell.component';
import { AppErrorStateComponent } from '../../shared/error-state/app-error-state.component';

@Component({
  selector: 'app-login',
  standalone: true,
  imports: [
    AppErrorStateComponent,
    AppShellComponent,
    IonButton,
    IonInput,
    IonItem,
    IonLabel,
    IonNote,
    IonSpinner,
    IonText,
    ReactiveFormsModule
  ],
  styles: [
    `
      .login-card {
        max-width: 420px;
        margin-inline: auto;
      }

      form {
        display: grid;
        gap: var(--app-space-3);
      }

      ion-item {
        --border-radius: var(--app-radius-sm);
      }

      ion-button {
        min-height: 44px;
      }

      .demo-divider {
        display: grid;
        grid-template-columns: 1fr auto 1fr;
        align-items: center;
        gap: var(--app-space-3);
        margin: var(--app-space-5) 0 var(--app-space-4);
        color: var(--ion-color-medium);
        font-size: 0.78rem;
        letter-spacing: 0.08em;
        text-transform: uppercase;
      }

      .demo-divider::before,
      .demo-divider::after {
        content: '';
        height: 1px;
        background: color-mix(in srgb, var(--ion-color-medium) 32%, transparent);
      }

      .demo-panel {
        padding: var(--app-space-4);
        border: 1px solid color-mix(in srgb, var(--ion-color-primary) 25%, transparent);
        border-radius: var(--app-radius-sm);
        background: color-mix(in srgb, var(--ion-color-primary) 6%, transparent);
      }

      .demo-panel h2 {
        margin: 0 0 var(--app-space-2);
        font-size: 1rem;
      }

      .demo-panel p {
        margin: 0 0 var(--app-space-3);
        line-height: 1.5;
      }
    `
  ],
  template: `
    <app-shell title="Iniciar sesión">
      <section class="login-card">
        <h1>Iniciar sesión</h1>
        <p>
          <ion-text color="medium">Usá tu cuenta de residente o administrador.</ion-text>
        </p>

        <form [formGroup]="form" (ngSubmit)="submit()" novalidate>
          <ion-item>
            <ion-label position="stacked">Correo electrónico</ion-label>
            <ion-input
              formControlName="email"
              type="email"
              inputmode="email"
              autocomplete="username"
              required
            />
          </ion-item>
          @if (email.invalid && (email.dirty || email.touched)) {
            <ion-note color="danger">Ingresá un correo electrónico válido.</ion-note>
          }

          <ion-item>
            <ion-label position="stacked">Contraseña</ion-label>
            <ion-input
              formControlName="password"
              type="password"
              autocomplete="current-password"
              required
            />
          </ion-item>
          @if (password.invalid && (password.dirty || password.touched)) {
            <ion-note color="danger">Ingresá tu contraseña.</ion-note>
          }

          @if (errorMessage()) {
            <app-error-state [title]="errorMessage()!" />
          }

          <ion-button
            type="submit"
            expand="block"
            [disabled]="form.invalid || loading() || demoLoading()"
          >
            @if (loading()) {
              <ion-spinner name="dots" aria-label="Iniciando sesión" />
            } @else {
              Iniciar sesión
            }
          </ion-button>
        </form>

        @if (demoAvailable()) {
          <div class="demo-divider"><span>o</span></div>
          <aside class="demo-panel" aria-label="Acceso a demo en vivo">
            <h2>Explorá la demo de residente</h2>
            <p>
              <ion-text color="medium">
                No requiere credenciales. El acceso es solo como residente con datos compartidos de staging;
                nunca se otorgan permisos de administrador.
              </ion-text>
            </p>
            <ion-button
              type="button"
              expand="block"
              fill="outline"
              [disabled]="loading() || demoLoading()"
              (click)="startDemo()"
            >
              @if (demoLoading()) {
                <ion-spinner name="dots" aria-label="Abriendo demo" />
              } @else {
                Abrir demo
              }
            </ion-button>
          </aside>
        }
      </section>
    </app-shell>
  `
})
export class LoginPage implements OnInit {
  private readonly auth = inject(AuthService);
  private readonly route = inject(ActivatedRoute);

  readonly loading = signal(false);
  readonly demoLoading = signal(false);
  readonly demoAvailable = signal(false);
  readonly errorMessage = signal<string | null>(null);
  readonly form = new FormGroup({
    email: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.email]
    }),
    password: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required]
    })
  });

  ngOnInit(): void {
    this.auth.demoStatus().subscribe({
      next: (enabled) => this.demoAvailable.set(enabled),
      error: () => this.demoAvailable.set(false)
    });
  }

  get email(): FormControl<string> {
    return this.form.controls.email;
  }

  get password(): FormControl<string> {
    return this.form.controls.password;
  }

  submit(): void {
    if (this.form.invalid || this.loading() || this.demoLoading()) {
      this.form.markAllAsTouched();
      return;
    }

    this.loading.set(true);
    this.errorMessage.set(null);

    const { email, password } = this.form.getRawValue();
    const returnUrl = this.route.snapshot.queryParamMap.get('returnUrl');

    this.auth.login({ email, password }, returnUrl).subscribe({
      next: () => {
        this.form.controls.password.reset('');
        this.loading.set(false);
      },
      error: (error: ApiError) => {
        this.form.controls.password.reset('');
        this.loading.set(false);
        this.errorMessage.set(loginErrorMessage(error));
      }
    });
  }

  startDemo(): void {
    if (!this.demoAvailable() || this.loading() || this.demoLoading()) {
      return;
    }

    this.demoLoading.set(true);
    this.errorMessage.set(null);

    const returnUrl = this.route.snapshot.queryParamMap.get('returnUrl');

    this.auth.loginDemo(returnUrl).subscribe({
      next: () => this.demoLoading.set(false),
      error: () => {
        this.demoLoading.set(false);
        this.errorMessage.set(
          'La demo en vivo no está disponible temporalmente. Intentá nuevamente.'
        );
      }
    });
  }
}

function loginErrorMessage(error: ApiError): string {
  if (error.status === 401) {
    return 'No pudimos iniciar sesión con esas credenciales.';
  }

  if (error.status === 0 || error.status >= 500) {
    return 'El servicio de inicio de sesión no está disponible. Intentá nuevamente.';
  }

  return 'No pudimos completar el inicio de sesión. Intentá nuevamente.';
}
