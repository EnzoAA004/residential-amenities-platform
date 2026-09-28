import { Component, Input } from '@angular/core';
import { IonContent, IonHeader, IonTitle, IonToolbar } from '@ionic/angular';

/**
 * The application's reusable page frame: a header with the page title and a
 * content area constrained to a comfortable reading width. Every top-level
 * page composes its content inside `<app-shell>` instead of repeating its
 * own `ion-header`/`ion-content` boilerplate.
 *
 * There is deliberately no primary navigation rendered here yet — issue #44
 * is infrastructure only. The header is where issue #45+ adds real
 * navigation (sign-out, resident/admin sections) once those routes exist;
 * nothing here needs to change shape to add it.
 */
@Component({
  selector: 'app-shell',
  standalone: true,
  imports: [IonContent, IonHeader, IonTitle, IonToolbar],
  template: `
    <ion-header>
      <ion-toolbar>
        <ion-title>{{ title }}</ion-title>
      </ion-toolbar>
    </ion-header>

    <ion-content class="ion-padding">
      <div class="app-shell__content">
        <ng-content></ng-content>
      </div>
    </ion-content>
  `,
  styleUrl: './app-shell.component.scss'
})
export class AppShellComponent {
  @Input({ required: true }) title = '';
}
