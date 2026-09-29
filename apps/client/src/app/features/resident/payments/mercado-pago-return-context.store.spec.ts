import { TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { MercadoPagoReturnContextStore } from './mercado-pago-return-context.store';

const STORAGE_KEY = 'residential-amenities:mercadopago-return';

describe('MercadoPagoReturnContextStore', () => {
  let store: MercadoPagoReturnContextStore;

  beforeEach(() => {
    sessionStorage.clear();
    TestBed.configureTestingModule({ providers: [MercadoPagoReturnContextStore] });
    store = TestBed.inject(MercadoPagoReturnContextStore);
  });

  afterEach(() => {
    sessionStorage.clear();
  });

  it('returns null when nothing was saved', () => {
    expect(store.read()).toBeNull();
  });

  it('saves and reads back exactly paymentId and reservationId', () => {
    const saved = store.save({ paymentId: 'payment-1', reservationId: 'reservation-1' });

    expect(saved).toBe(true);
    expect(store.read()).toEqual({ paymentId: 'payment-1', reservationId: 'reservation-1' });
  });

  it('returns false, and persists nothing, when sessionStorage.setItem throws', () => {
    const setItemSpy = vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('storage unavailable');
    });

    try {
      const saved = store.save({ paymentId: 'payment-1', reservationId: 'reservation-1' });
      expect(saved).toBe(false);
    } finally {
      setItemSpy.mockRestore();
    }

    expect(sessionStorage.getItem(STORAGE_KEY)).toBeNull();
    expect(store.read()).toBeNull();
  });

  it('stores only the two ids in sessionStorage — nothing else', () => {
    store.save({ paymentId: 'payment-1', reservationId: 'reservation-1' });

    const raw = sessionStorage.getItem(STORAGE_KEY);
    expect(raw).not.toBeNull();
    expect(JSON.parse(raw!)).toEqual({ paymentId: 'payment-1', reservationId: 'reservation-1' });
  });

  it('overwrites a previous entry with a new save', () => {
    store.save({ paymentId: 'payment-1', reservationId: 'reservation-1' });
    store.save({ paymentId: 'payment-2', reservationId: 'reservation-2' });

    expect(store.read()).toEqual({ paymentId: 'payment-2', reservationId: 'reservation-2' });
  });

  it('clears the entry', () => {
    store.save({ paymentId: 'payment-1', reservationId: 'reservation-1' });
    store.clear();

    expect(store.read()).toBeNull();
    expect(sessionStorage.getItem(STORAGE_KEY)).toBeNull();
  });

  it('returns null and clears malformed JSON instead of throwing', () => {
    sessionStorage.setItem(STORAGE_KEY, '{not valid json');

    expect(store.read()).toBeNull();
    expect(sessionStorage.getItem(STORAGE_KEY)).toBeNull();
  });

  it('returns null and clears a partial object missing a required field', () => {
    sessionStorage.setItem(STORAGE_KEY, JSON.stringify({ paymentId: 'payment-1' }));

    expect(store.read()).toBeNull();
    expect(sessionStorage.getItem(STORAGE_KEY)).toBeNull();
  });

  it('returns null and clears a non-object value', () => {
    sessionStorage.setItem(STORAGE_KEY, JSON.stringify('just-a-string'));

    expect(store.read()).toBeNull();
    expect(sessionStorage.getItem(STORAGE_KEY)).toBeNull();
  });

  it('never stores a checkoutUrl, amount, currency, or status even if passed extra fields', () => {
    store.save({
      paymentId: 'payment-1',
      reservationId: 'reservation-1',
      // @ts-expect-error -- extra fields are not part of the type; verifying they are dropped anyway
      checkoutUrl: 'https://mercadopago.test/checkout/order-1',
      amount: 5000,
      status: 'Approved'
    });

    const raw = sessionStorage.getItem(STORAGE_KEY);
    expect(raw).not.toContain('checkoutUrl');
    expect(raw).not.toContain('mercadopago.test');
    expect(raw).not.toContain('amount');
    expect(raw).not.toContain('status');
  });
});
