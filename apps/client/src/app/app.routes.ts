import { Routes } from '@angular/router';

/**
 * Flat for now — issue #44 is infrastructure only, so there is no
 * `/login` or authenticated section yet. Issue #45 adds those without
 * needing to restructure this file: a `/login` route plus guarded route
 * groups for the resident (`/app/...`) and admin (`/admin/...`) experiences
 * slot in alongside `''` and `diagnostics` the same way they are defined
 * here (lazy-loaded standalone components).
 */
export const routes: Routes = [
  {
    path: '',
    loadComponent: () => import('./features/landing/landing.page').then((module) => module.LandingPage)
  },
  {
    path: 'diagnostics',
    loadComponent: () =>
      import('./features/diagnostics/diagnostics.page').then((module) => module.DiagnosticsPage)
  },
  {
    path: '**',
    redirectTo: ''
  }
];
