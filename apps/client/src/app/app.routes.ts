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
    loadComponent: () =>
      import('./features/admin/admin-shell.page').then((module) => module.AdminShellPage),
    children: [
      {
        path: '',
        pathMatch: 'full',
        loadComponent: () =>
          import('./features/admin/admin-overview.page').then((module) => module.AdminOverviewPage)
      },
      {
        path: 'reservations',
        pathMatch: 'full',
        loadComponent: () =>
          import('./features/admin/reservations/admin-reservations.page').then(
            (module) => module.AdminReservationsPage
          )
      },
      {
        path: 'reservations/:id',
        loadComponent: () =>
          import('./features/admin/reservations/admin-reservation-detail.page').then(
            (module) => module.AdminReservationDetailPage
          )
      },
      {
        path: 'payments',
        loadComponent: () =>
          import('./features/admin/payments/admin-payments.page').then(
            (module) => module.AdminPaymentsPage
          )
      },
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
