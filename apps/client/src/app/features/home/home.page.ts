import { Component, OnInit, inject, signal } from '@angular/core';
import {
  IonButton,
  IonContent,
  IonHeader,
  IonText,
  IonTitle,
  IonToolbar
} from '@ionic/angular';

import { HealthService } from '../../core/health/health.service';

@Component({
  selector: 'app-home',
  standalone: true,
  imports: [
    IonButton,
    IonContent,
    IonHeader,
    IonText,
    IonTitle,
    IonToolbar
  ],
  styles: [`
    .page {
      width: min(100%, 720px);
      margin: 0 auto;
      padding-block: 1rem 2rem;
    }

    .status-grid {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(180px, 1fr));
      gap: 1rem;
      margin-block: 1.5rem;
    }

    .status-card {
      border: 1px solid var(--ion-color-step-200, #d7d8da);
      border-radius: 12px;
      padding: 1rem;
    }

    .status-card p {
      margin-bottom: 0;
    }
  `],
  template: `
    <ion-header>
      <ion-toolbar>
        <ion-title>Residential Amenities</ion-title>
      </ion-toolbar>
    </ion-header>

    <ion-content class="ion-padding">
      <main class="page">
        <h1>Application foundation</h1>
        <p>
          Angular + Ionic → ASP.NET Core → PostgreSQL
        </p>

        <div class="status-grid" aria-live="polite">
          <section class="status-card">
            <strong>API</strong>
            <p>
              <ion-text [color]="status() === 'ok' ? 'success' : 'medium'">
                {{ status() }}
              </ion-text>
            </p>
          </section>

          <section class="status-card">
            <strong>Database</strong>
            <p>
              <ion-text [color]="database() === 'ok' ? 'success' : 'medium'">
                {{ database() }}
              </ion-text>
            </p>
          </section>
        </div>

        <ion-button (click)="checkHealth()" [disabled]="checking()">
          {{ checking() ? 'Checking…' : 'Check API' }}
        </ion-button>
      </main>
    </ion-content>
  `
})
export class HomePage implements OnInit {
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
