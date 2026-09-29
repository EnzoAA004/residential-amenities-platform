import { Routes } from '@angular/router';

import { adminGuard } from './core/auth/admin.guard';
import { anonymousGuard, authenticatedGuard } from './core/auth/auth.guard';

export const routes: Routes = [
  {
    path: 'login',
    canMatch: [anonymousGuard],
    loadComponent: () => import('./features/login/login.page').then((module) => module.LoginPage)
  },
  {
    path: '',
    canMatch: [authenticatedGuard],
    loadComponent: () => import('./features/landing/landing.page').then((module) => module.LandingPage)
  },
  {
    path: 'amenities',
    canMatch: [authenticatedGuard],
    loadComponent: () =>
      import('./features/resident/amenities/amenities.page').then((module) => module.AmenitiesPage)
  },
  {
    path: 'reservations/:reservationId/payment',
    canMatch: [authenticatedGuard],
    loadComponent: () =>
      import('./features/resident/payments/payment.page').then((module) => module.PaymentPage)
  },
  {
    path: 'payments/return',
    canMatch: [authenticatedGuard],
    loadComponent: () =>
      import('./features/resident/payments/payment-return.page').then(
        (module) => module.PaymentReturnPage
      )
  },
  {
    path: 'admin',
    canMatch: [adminGuard],
    children: [
      {
        path: '**',
        redirectTo: ''
      }
    ]
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
