import { Component } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { IonButton } from '@ionic/angular';

import { AppShellComponent } from '../../layout/app-shell/app-shell.component';

@Component({
  selector: 'app-admin-shell-page',
  standalone: true,
  imports: [AppShellComponent, IonButton, RouterLink, RouterLinkActive, RouterOutlet],
  styles: [
    `
      .admin-nav {
        display: flex;
        flex-wrap: wrap;
        gap: var(--app-space-2);
        margin-block-end: var(--app-space-4);
      }

      .admin-nav ion-button.active {
        font-weight: 700;
      }
    `
  ],
  template: `
    <app-shell title="Administración">
      <nav class="admin-nav" aria-label="Administración">
        <ion-button routerLink="/admin" routerLinkActive="active" [routerLinkActiveOptions]="{ exact: true }">
          Resumen
        </ion-button>
        <ion-button routerLink="/admin/reservations" routerLinkActive="active">Reservas</ion-button>
        <ion-button routerLink="/admin/payments" routerLinkActive="active">Pagos</ion-button>
      </nav>

      <router-outlet />
    </app-shell>
  `
})
export class AdminShellPage {}
