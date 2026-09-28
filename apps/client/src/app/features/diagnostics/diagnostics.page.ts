import { Component, OnInit, inject, signal } from '@angular/core';
import { IonButton, IonText } from '@ionic/angular';

import { HealthService } from '../../core/health/health.service';
import { AppShellComponent } from '../../layout/app-shell/app-shell.component';

/**
 * Internal operational diagnostic (not the app's landing page — see
 * `features/landing`). Reachable at `/diagnostics` for development/ops use;
 * intentionally not linked from any product navigation.
 */
@Component({
  selector: 'app-diagnostics',
  standalone: true,
  imports: [AppShellComponent, IonButton, IonText],
  styles: [
    `
      .status-grid {
        display: grid;
        grid-template-columns: repeat(auto-fit, minmax(180px, 1fr));
        gap: var(--app-space-3);
        margin-block: var(--app-space-4);
      }

      .status-card {
        border: 1px solid var(--app-border-color);
        border-radius: var(--app-radius-md);
        padding: var(--app-space-3);
      }

      .status-card p {
        margin-bottom: 0;
      }
    `
  ],
  template: `
    <app-shell title="Diagnostics">
      <p>Internal operational check against the backend's root-level <code>/health</code> endpoint.</p>

      <div class="status-grid" aria-live="polite">
        <section class="status-card">
          <strong>API</strong>
          <p>
            <ion-text [color]="status() === 'ok' ? 'success' : 'medium'">{{ status() }}</ion-text>
          </p>
        </section>

        <section class="status-card">
          <strong>Database</strong>
          <p>
            <ion-text [color]="database() === 'ok' ? 'success' : 'medium'">{{ database() }}</ion-text>
          </p>
        </section>
      </div>

      <ion-button (click)="checkHealth()" [disabled]="checking()">
        {{ checking() ? 'Checking…' : 'Check API' }}
      </ion-button>
    </app-shell>
  `
})
export class DiagnosticsPage implements OnInit {
  private readonly health = inject(HealthService);

  readonly status = signal('not checked');
  readonly database = signal('not checked');
  readonly checking = signal(false);

  ngOnInit(): void {
    this.checkHealth();
  }

  checkHealth(): void {
    this.checking.set(true);
    this.status.set('checking');
    this.database.set('checking');

    this.health.check().subscribe({
      next: (response) => {
        this.status.set(response.status);
        this.database.set(response.database);
        this.checking.set(false);
      },
      error: () => {
        this.status.set('unavailable');
        this.database.set('unknown');
        this.checking.set(false);
      }
    });
  }
}
