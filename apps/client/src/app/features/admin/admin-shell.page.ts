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
        <ion-button routerLink="/admin/pricing" routerLinkActive="active">Precios</ion-button>
        <ion-button routerLink="/admin/availability" routerLinkActive="active">Disponibilidad</ion-button>
        <ion-button routerLink="/admin/event-slots" routerLinkActive="active">Turnos de eventos</ion-button>
        <ion-button routerLink="/admin/audit" routerLinkActive="active">Auditoría</ion-button>
        <ion-button routerLink="/admin/payment-review" routerLinkActive="active">Revisión manual</ion-button>
      </nav>

      <router-outlet />
    </app-shell>
  `
})
export class AdminShellPage {}
