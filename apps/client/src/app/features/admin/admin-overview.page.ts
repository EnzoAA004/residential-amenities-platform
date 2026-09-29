import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import { IonButton, IonText } from '@ionic/angular';

@Component({
  selector: 'app-admin-overview-page',
  standalone: true,
  imports: [IonButton, IonText, RouterLink],
  styles: [
    `
      .admin-overview {
        display: grid;
        gap: var(--app-space-3);
      }

      .admin-overview__actions {
        display: flex;
        flex-wrap: wrap;
        gap: var(--app-space-2);
      }
    `
  ],
  template: `
    <section class="admin-overview" aria-labelledby="admin-overview-title">
      <h1 id="admin-overview-title">Resumen</h1>
      <p>
        <ion-text color="medium">Acceso read-only a reservas y pagos administrativos.</ion-text>
      </p>
      <div class="admin-overview__actions">
        <ion-button routerLink="/admin/reservations">Ver reservas</ion-button>
        <ion-button routerLink="/admin/payments" fill="outline">Ver pagos</ion-button>
      </div>
    </section>
  `
})
export class AdminOverviewPage {}
