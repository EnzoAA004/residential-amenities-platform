import { describe, expect, it } from 'vitest';

import { adminGuard } from './core/auth/admin.guard';
import { authenticatedGuard } from './core/auth/auth.guard';
import { routes } from './app.routes';

describe('app routes', () => {
  it('protects resident reservation history/detail routes and keeps payment before generic detail', () => {
    const listIndex = routes.findIndex((route) => route.path === 'reservations');
    const paymentIndex = routes.findIndex((route) => route.path === 'reservations/:reservationId/payment');
    const detailIndex = routes.findIndex((route) => route.path === 'reservations/:reservationId');

    expect(routes[listIndex].canMatch).toEqual([authenticatedGuard]);
    expect(routes[paymentIndex].canMatch).toEqual([authenticatedGuard]);
    expect(routes[detailIndex].canMatch).toEqual([authenticatedGuard]);
    expect(paymentIndex).toBeLessThan(detailIndex);
  });

  it('leaves the admin parent guarded by adminGuard', () => {
    expect(routes.find((route) => route.path === 'admin')?.canMatch).toEqual([adminGuard]);
  });
});
