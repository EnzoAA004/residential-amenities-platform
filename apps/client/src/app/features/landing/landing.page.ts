import { Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { IonButton, IonButtons, IonText } from '@ionic/angular';

import { AuthSessionStore } from '../../core/auth/auth-session.store';
import { AuthService } from '../../core/auth/auth.service';
import { AppShellComponent } from '../../layout/app-shell/app-shell.component';

/**
 * The application's real landing page — a neutral product entry point, not
 * an infrastructure/health screen. Authentication and the resident/admin
 * experiences it will lead to arrive in issue #45 and later.
 */
@Component({
  selector: 'app-landing',
  standalone: true,
  imports: [AppShellComponent, IonButton, IonButtons, IonText, RouterLink],
  template: `
    <app-shell title="Residential Amenities">
      @if (currentUser(); as user) {
        <ion-buttons shell-actions slot="end">
          <ion-text color="medium" class="landing-user">{{ user.displayName }}</ion-text>
          <ion-button type="button" fill="clear" (click)="logout()" [disabled]="loggingOut">
            {{ loggingOut ? 'Signing out…' : 'Sign out' }}
          </ion-button>
        </ion-buttons>
      }

      <h1>Welcome, {{ displayName() }}</h1>
      <p>
        Your web session is active. Reservation, payment and admin workflows
        will arrive in the next product slices.
      </p>
      @if (memberships().length > 0) {
        <p>
          <ion-text color="medium">
            Active memberships: {{ membershipLabels() }}
          </ion-text>
        </p>
      }
      <p>
        <ion-button routerLink="/amenities">View amenities</ion-button>
      </p>
    </app-shell>
  `,
  styles: [
    `
      .landing-user {
        display: none;
        max-width: 18rem;
        overflow: hidden;
        text-overflow: ellipsis;
        white-space: nowrap;
      }

      @media (min-width: 520px) {
        .landing-user {
          display: inline-block;
        }
      }
    `
  ]
})
export class LandingPage {
  private readonly auth = inject(AuthService);
  private readonly session = inject(AuthSessionStore);

  readonly currentUser = this.session.currentUser;
  readonly memberships = this.session.memberships;
  loggingOut = false;

  displayName(): string {
    return this.session.currentUser()?.displayName ?? 'there';
  }

  membershipLabels(): string {
    return this.memberships()
      .map((membership) => `${membership.building} ${membership.unit}`)
      .join(', ');
  }

  logout(): void {
    this.loggingOut = true;

    this.auth.logout().subscribe({
      next: () => {
        this.loggingOut = false;
      },
      error: () => {
        this.loggingOut = false;
      }
    });
  }
}
