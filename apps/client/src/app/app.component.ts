import { HttpClient } from '@angular/common/http';
import { Component, OnInit, inject } from '@angular/core';
import {
  IonApp,
  IonButton,
  IonContent,
  IonHeader,
  IonText,
  IonTitle,
  IonToolbar
} from '@ionic/angular';

type HealthResponse = {
  status: string;
  database: string;
  utc: string;
};

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [
    IonApp,
    IonButton,
    IonContent,
    IonHeader,
    IonText,
    IonTitle,
    IonToolbar
  ],
  template: `
    <ion-app>
      <ion-header>
        <ion-toolbar>
          <ion-title>Residential Amenities</ion-title>
        </ion-toolbar>
      </ion-header>

      <ion-content class="ion-padding">
        <h1>Toolchain spike</h1>
        <p>Angular + Ionic → ASP.NET Core → PostgreSQL</p>

        <ion-button (click)="checkHealth()">Check API</ion-button>

        <p>
          API:
          <ion-text [color]="status === 'ok' ? 'success' : 'medium'">
            {{ status }}
          </ion-text>
        </p>

        <p>
          Database:
          <ion-text [color]="database === 'ok' ? 'success' : 'medium'">
            {{ database }}
          </ion-text>
        </p>
      </ion-content>
    </ion-app>
  `
})
export class AppComponent implements OnInit {
  private readonly http = inject(HttpClient);

  status = 'not checked';
  database = 'not checked';

  ngOnInit(): void {
    this.checkHealth();
  }

  checkHealth(): void {
    this.status = 'checking';
    this.database = 'checking';

    this.http.get<HealthResponse>('/api/health').subscribe({
      next: (response) => {
        this.status = response.status;
        this.database = response.database;
      },
      error: () => {
        this.status = 'unavailable';
        this.database = 'unknown';
      }
    });
  }
}
