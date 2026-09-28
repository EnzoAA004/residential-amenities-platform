import { Component } from '@angular/core';
import { IonText } from '@ionic/angular';

import { AppShellComponent } from '../../layout/app-shell/app-shell.component';

/**
 * The application's real landing page — a neutral product entry point, not
 * an infrastructure/health screen. Authentication and the resident/admin
 * experiences it will lead to arrive in issue #45 and later.
 */
@Component({
  selector: 'app-landing',
  standalone: true,
  imports: [AppShellComponent, IonText],
  template: `
    <app-shell title="Residential Amenities">
      <h1>Residential Amenities Platform</h1>
      <p>
        Book shared amenities, manage your reservations and keep track of
        payments for your building — all in one place.
      </p>
      <p>
        <ion-text color="medium">Frontend foundation — product screens are being built.</ion-text>
      </p>
    </app-shell>
  `
})
export class LandingPage {}
