import { Component, inject, signal } from '@angular/core';
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
    `
  ],
  template: `
    <app-shell title="Sign in">
      <section class="login-card">
        <h1>Sign in</h1>
        <p>
          <ion-text color="medium">Use your resident or administrator account.</ion-text>
        </p>

        <form [formGroup]="form" (ngSubmit)="submit()" novalidate>
          <ion-item>
            <ion-label position="stacked">Email</ion-label>
            <ion-input
              formControlName="email"
              type="email"
              inputmode="email"
              autocomplete="username"
              required
            />
          </ion-item>
          @if (email.invalid && (email.dirty || email.touched)) {
            <ion-note color="danger">Enter a valid email address.</ion-note>
          }

          <ion-item>
            <ion-label position="stacked">Password</ion-label>
            <ion-input
              formControlName="password"
              type="password"
              autocomplete="current-password"
              required
            />
          </ion-item>
          @if (password.invalid && (password.dirty || password.touched)) {
            <ion-note color="danger">Enter your password.</ion-note>
          }

          @if (errorMessage()) {
            <app-error-state [title]="errorMessage()!" />
          }

          <ion-button type="submit" expand="block" [disabled]="form.invalid || loading()">
            @if (loading()) {
              <ion-spinner name="dots" aria-label="Signing in" />
            } @else {
              Sign in
            }
          </ion-button>
        </form>
      </section>
    </app-shell>
  `
})
export class LoginPage {
  private readonly auth = inject(AuthService);
  private readonly route = inject(ActivatedRoute);

  readonly loading = signal(false);
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

  get email(): FormControl<string> {
    return this.form.controls.email;
  }

  get password(): FormControl<string> {
    return this.form.controls.password;
  }

  submit(): void {
    if (this.form.invalid || this.loading()) {
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
}

function loginErrorMessage(error: ApiError): string {
  if (error.status === 401) {
    return 'We could not sign you in with those credentials.';
  }

  if (error.status === 0 || error.status >= 500) {
    return 'The sign-in service is unavailable. Please try again.';
  }

  return 'We could not complete sign-in. Please try again.';
}

