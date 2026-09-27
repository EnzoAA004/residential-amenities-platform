import { provideHttpClient } from '@angular/common/http';
import { ApplicationConfig } from '@angular/core';
import {
  RouteReuseStrategy,
  provideRouter
} from '@angular/router';
import {
  IonicRouteStrategy,
  provideIonicAngular
} from '@ionic/angular';

import { environment } from '../environments/environment';
import { routes } from './app.routes';
import { API_BASE_URL } from './core/config/api-base-url.token';

export const appConfig: ApplicationConfig = {
  providers: [
    { provide: RouteReuseStrategy, useClass: IonicRouteStrategy },
    provideHttpClient(),
    provideIonicAngular(),
    provideRouter(routes),
    {
      provide: API_BASE_URL,
      useValue: environment.apiBaseUrl
    }
  ]
};
