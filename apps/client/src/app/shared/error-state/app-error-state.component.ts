import { Component, EventEmitter, Input, Output } from '@angular/core';
import { IonButton, IonText } from '@ionic/angular';

/**
 * Unified rendering for an API error: a required `title`, an optional
 * `detail` (the backend's own `ProblemDetails.detail`, shown verbatim —
 * never clipped or hidden), and an optional retry action.
 *
 * This is a drop-in replacement for the many local
 * `<p role="alert"><ion-text color="danger">{{ ... }}</ion-text></p>` blocks
 * scattered across pages. It never decides on its own whether retry is
 * safe — a caller only binds `(retry)` where retrying was already the
 * established behavior (e.g. reloading a GET), never for a non-idempotent
 * mutation that didn't offer retry before.
 */
@Component({
  selector: 'app-error-state',
  standalone: true,
  imports: [IonButton, IonText],
  styles: [
    `
      :host {
        display: block;
      }

      .app-error-state__message {
        display: block;
        margin-block-end: var(--app-space-2);
      }
    `
  ],
  template: `
    <p role="alert" class="app-error-state__message">
      <ion-text color="danger">{{ title }}</ion-text>
      @if (detail) {
        <br />
        <ion-text color="danger">{{ detail }}</ion-text>
      }
    </p>
    @if (showRetry) {
      <ion-button type="button" fill="outline" (click)="retry.emit()">{{ retryLabel }}</ion-button>
    }
  `
})
export class AppErrorStateComponent {
  @Input({ required: true }) title = '';
  @Input() detail?: string;
  /**
   * Whether to render the retry button. Defaults to `false` — a page must
   * opt in explicitly, so retry is never offered where it wasn't before.
   */
  @Input() showRetry = false;
  @Input() retryLabel = 'Reintentar';

  @Output() readonly retry = new EventEmitter<void>();
}
