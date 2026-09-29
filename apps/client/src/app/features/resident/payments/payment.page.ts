import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toObservable, toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute } from '@angular/router';
import { IonButton, IonNote, IonSpinner, IonText } from '@ionic/angular';
import { Subject, catchError, map, merge, of, startWith, switchMap } from 'rxjs';

import { ApiError } from '../../../core/api/api-error';
import { AppShellComponent } from '../../../layout/app-shell/app-shell.component';
import { ExternalNavigationService } from './external-navigation.service';
import { MercadoPagoReturnContextStore } from './mercado-pago-return-context.store';
import { Payment } from './payment.models';
import { PaymentService } from './payment.service';
import { Reservation } from '../reservations/reservation.models';

type ReservationLoadState =
  | { status: 'loading' }
  | { status: 'success'; reservation: Reservation }
  | { status: 'error'; error: ApiError };

/**
 * Which payment method is currently submitting, if any. Both actions are
 * disabled while either is `'submitting*'` — the backend rejects a second
 * active method with 409 anyway, but the UI should never invite that.
 * `'error'` is always a safe state to retry from: unlike reservation
 * creation (#47/#48), these two POSTs are backend-idempotent (Mercado Pago
 * reuses the same persisted attempt/idempotency key; Cash returns the same
 * still-`Pending` declaration), so a manual retry — including after a
 * network failure — is always safe. There is deliberately no `'unknown'`
 * state here.
 */
type PaymentActionState =
  | { status: 'idle' }
  | { status: 'submittingMercadoPago' }
  | { status: 'submittingCash' }
  | { status: 'error'; method: 'MercadoPago' | 'Cash'; error: ApiError };

type CashPaymentState =
  | { status: 'idle' }
  | { status: 'loading' }
  | { status: 'success'; payment: Payment }
  | { status: 'error'; error: ApiError };

const PAYMENT_STATUS_LABELS: Record<Payment['status'], string> = {
  Created: 'Iniciando',
  Pending: 'Pendiente',
  Approved: 'Aprobado',
  Rejected: 'Rechazado',
  Cancelled: 'Cancelado'
};

const RESERVATION_OUTCOME_EXPLANATIONS: Record<Payment['reservationOutcome'], string> = {
  None: '',
  ReservationConfirmed: 'La reserva fue confirmada.',
  ApprovedAfterExpiry: 'El pago se acreditó después de que venciera el plazo de la reserva.',
  ApprovedForCancelledReservation: 'El pago se acreditó pero la reserva ya estaba cancelada.',
  ApprovedForMissingReservation: 'El pago se acreditó pero no se encontró la reserva asociada.'
};

/**
 * The payment page for a `Pending` reservation (issue #49): choose Mercado
 * Pago or cash, or check on a cash declaration already made. `reservationId`
 * always comes from the route — never from a signal a previous component
 * left behind — so this page is reloadable and independently reachable
 * (direct navigation, a login redirect's `returnUrl`, ...), subject to the
 * same backend authorization as everything else.
 *
 * The device clock is never authoritative: the hold countdown is purely
 * informational, and reaching zero never locally marks the reservation
 * `Expired` — only a real backend response does that.
 */
@Component({
  selector: 'app-payment-page',
  standalone: true,
  imports: [AppShellComponent, IonButton, IonNote, IonSpinner, IonText],
  styles: [
    `
      .payment-card,
      .method-card {
        border: 1px solid var(--app-border-color);
        border-radius: var(--app-radius-md);
        padding: var(--app-space-3);
        margin-block: var(--app-space-3);
      }

      .payment-card dl {
        display: grid;
        grid-template-columns: auto 1fr;
        gap: var(--app-space-1) var(--app-space-3);
        margin: 0;
      }

      .payment-card dt {
        font-weight: 600;
      }

      .payment-card dd {
        margin: 0;
      }

      .method-actions {
        display: flex;
        gap: var(--app-space-3);
        flex-wrap: wrap;
      }
    `
  ],
  template: `
    <app-shell title="Pago de la reserva">
      @switch (reservationState().status) {
        @case ('loading') {
          <p aria-live="polite">
            <ion-spinner name="dots" /> <ion-text color="medium">Cargando reserva…</ion-text>
          </p>
        }
        @case ('error') {
          <p role="alert">
            <ion-text color="danger">{{ reservationErrorTitle() }}</ion-text>
            @if (reservationErrorDetail(); as detail) {
              <br />
              <ion-text color="danger">{{ detail }}</ion-text>
            }
          </p>
          <ion-button type="button" fill="clear" (click)="reloadReservation()">Retry</ion-button>
        }
        @case ('success') {
          <section class="payment-card">
            <dl>
              <dt>Reservation</dt>
              <dd>{{ reservation()!.useType }}</dd>
              <dt>Status</dt>
              <dd>{{ reservation()!.status }}</dd>
              <dt>Total</dt>
              <dd>{{ formatAmount(reservation()!.totalAmount, reservation()!.currency) }}</dd>
            </dl>

            @if (isPending()) {
              <p>
                @if (remainingMs(); as remaining) {
                  <ion-text [color]="remaining > 0 ? 'medium' : 'warning'">
                    @if (remaining > 0) {
                      Tiempo restante del hold: {{ formatRemaining(remaining) }}
                    } @else {
                      El plazo indicado llegó a cero. El servidor validará el estado real cuando
                      intentes continuar o actualices la reserva.
                    }
                  </ion-text>
                }
              </p>
            }
          </section>

          @if (!isPending()) {
            <p>
              <ion-text color="medium">
                Esta reserva no está pendiente de pago (estado actual: {{ reservation()!.status }}).
              </ion-text>
            </p>
          } @else if (cashPaymentState(); as cashState) {
            @if (cashState.status === 'success') {
              <section class="payment-card" aria-live="polite">
                <h3>Efectivo declarado</h3>
                <dl>
                  <dt>Método</dt>
                  <dd>Efectivo</dd>
                  <dt>Estado del pago</dt>
                  <dd>{{ paymentStatusLabel(cashState.payment.status) }}</dd>
                  <dt>Monto</dt>
                  <dd>{{ formatAmount(cashState.payment.amount, cashState.payment.currency) }}</dd>
                  @if (cashState.payment.cashConfirmedAtUtc; as confirmedAt) {
                    <dt>Confirmado</dt>
                    <dd>{{ formatDateTime(confirmedAt) }}</dd>
                  }
                </dl>

                @if (cashState.payment.status === 'Pending') {
                  <p>
                    <ion-text color="medium">
                      La declaración de efectivo todavía NO confirma la reserva. Un administrador
                      debe confirmar que recibió el dinero.
                    </ion-text>
                  </p>
                } @else if (
                  cashState.payment.status === 'Approved' &&
                  cashState.payment.reservationOutcome === 'ReservationConfirmed'
                ) {
                  <p>
                    <ion-text color="success">Pago aprobado y reserva confirmada.</ion-text>
                  </p>
                } @else if (cashState.payment.requiresManualReview) {
                  <p role="alert">
                    <ion-note color="warning">
                      El pago fue aprobado, pero requiere revisión manual.
                      {{ reservationOutcomeExplanation(cashState.payment.reservationOutcome) }}
                    </ion-note>
                  </p>
                }

                <ion-button type="button" fill="clear" (click)="refreshCashPayment()">
                  Actualizar estado
                </ion-button>
              </section>
            } @else {
            <section class="method-card">
              <h3>Elegí cómo pagar</h3>
              <div class="method-actions">
                <ion-button
                  type="button"
                  [disabled]="isSubmitting()"
                  (click)="initiateMercadoPago()"
                >
                  {{
                    actionState().status === 'submittingMercadoPago'
                      ? 'Iniciando Mercado Pago…'
                      : 'Pagar con Mercado Pago'
                  }}
                </ion-button>

                <ion-button
                  type="button"
                  fill="outline"
                  [disabled]="isSubmitting()"
                  (click)="declareCash()"
                >
                  {{ actionState().status === 'submittingCash' ? 'Declarando…' : 'Declarar efectivo' }}
                </ion-button>
              </div>

              @if (actionState(); as action) {
                @if (action.status === 'error') {
                  <p role="alert">
                    <ion-text color="danger">{{ action.error.title }}</ion-text>
                    @if (action.error.detail; as detail) {
                      <br />
                      <ion-text color="danger">{{ detail }}</ion-text>
                    }
                  </p>
                  <p>
                    <ion-note color="medium">
                      Podés volver a intentarlo: el servidor reutiliza el intento activo de este
                      método.
                    </ion-note>
                  </p>
                }
              }

              @if (cashPaymentState().status === 'loading') {
                <p aria-live="polite">
                  <ion-spinner name="dots" /> <ion-text color="medium">Consultando el pago…</ion-text>
                </p>
              }

              @if (cashPaymentState().status === 'error') {
                <p role="alert">
                  <ion-text color="danger">{{ cashPaymentErrorTitle() }}</ion-text>
                </p>
              }
            </section>
          }
        }
      }
    }
    </app-shell>
  `
})
export class PaymentPage {
  private readonly route = inject(ActivatedRoute);
  private readonly paymentService = inject(PaymentService);
  private readonly returnContextStore = inject(MercadoPagoReturnContextStore);
  private readonly externalNavigation = inject(ExternalNavigationService);
  private readonly destroyRef = inject(DestroyRef);

  private readonly routeReservationId = toSignal(
    this.route.paramMap.pipe(map((params) => params.get('reservationId'))),
    { initialValue: null as string | null }
  );

  readonly reservationId = computed(() => this.routeReservationId() ?? '');

  private readonly reservationRetrySubject = new Subject<void>();

  readonly reservationState = toSignal(
    merge(
      toObservable(this.reservationId),
      this.reservationRetrySubject.pipe(map(() => this.reservationId()))
    ).pipe(
      switchMap((reservationId) => {
        if (!reservationId) {
          return of<ReservationLoadState>({
            status: 'error',
            error: { status: 400, title: 'Falta el identificador de la reserva.' }
          });
        }

        return this.paymentService.getReservation(reservationId).pipe(
          map((reservation): ReservationLoadState => ({ status: 'success', reservation })),
          catchError((error: ApiError) => of<ReservationLoadState>({ status: 'error', error })),
          startWith<ReservationLoadState>({ status: 'loading' })
        );
      })
    ),
    { initialValue: { status: 'loading' } as ReservationLoadState }
  );

  readonly reservation = computed(() => {
    const state = this.reservationState();
    return state.status === 'success' ? state.reservation : null;
  });

  readonly isPending = computed(() => this.reservation()?.status === 'Pending');

  readonly reservationErrorTitle = computed(() => {
    const state = this.reservationState();
    return state.status === 'error' ? state.error.title : '';
  });

  readonly reservationErrorDetail = computed(() => {
    const state = this.reservationState();
    return state.status === 'error' ? (state.error.detail ?? null) : null;
  });

  readonly actionState = signal<PaymentActionState>({ status: 'idle' });

  readonly isSubmitting = computed(() => {
    const status = this.actionState().status;
    return status === 'submittingMercadoPago' || status === 'submittingCash';
  });

  private readonly cashPaymentIdSignal = signal<string | null>(null);
  private readonly cashRefreshSubject = new Subject<void>();

  readonly cashPaymentState = toSignal(
    merge(
      toObservable(this.cashPaymentIdSignal),
      this.cashRefreshSubject.pipe(map(() => this.cashPaymentIdSignal()))
    ).pipe(
      switchMap((paymentId) => {
        if (!paymentId) {
          return of<CashPaymentState>({ status: 'idle' });
        }

        return this.paymentService.getPayment(paymentId).pipe(
          map((payment): CashPaymentState => ({ status: 'success', payment })),
          catchError((error: ApiError) => of<CashPaymentState>({ status: 'error', error })),
          startWith<CashPaymentState>({ status: 'loading' })
        );
      })
    ),
    { initialValue: { status: 'idle' } as CashPaymentState }
  );

  readonly cashPaymentErrorTitle = computed(() => {
    const state = this.cashPaymentState();
    return state.status === 'error' ? state.error.title : '';
  });

  private readonly nowMs = signal(Date.now());

  constructor() {
    const timer = setInterval(() => this.nowMs.set(Date.now()), 1000);
    this.destroyRef.onDestroy(() => clearInterval(timer));
  }

  readonly remainingMs = computed<number | null>(() => {
    const reservation = this.reservation();

    if (!reservation) {
      return null;
    }

    const expiresAtMs = new Date(reservation.expiresAtUtc).getTime();
    return Math.max(0, expiresAtMs - this.nowMs());
  });

  reloadReservation(): void {
    this.reservationRetrySubject.next();
  }

  refreshCashPayment(): void {
    this.cashRefreshSubject.next();
  }

  paymentStatusLabel(status: Payment['status']): string {
    return PAYMENT_STATUS_LABELS[status];
  }

  reservationOutcomeExplanation(outcome: Payment['reservationOutcome']): string {
    return RESERVATION_OUTCOME_EXPLANATIONS[outcome];
  }

  formatAmount(amount: number, currency: string): string {
    try {
      return new Intl.NumberFormat(undefined, { style: 'currency', currency }).format(amount);
    } catch {
      return `${amount} ${currency}`;
    }
  }

  formatDateTime(isoValue: string): string {
    return new Intl.DateTimeFormat(undefined, { dateStyle: 'medium', timeStyle: 'short' }).format(
      new Date(isoValue)
    );
  }

  formatRemaining(ms: number): string {
    const totalSeconds = Math.floor(ms / 1000);
    const hours = Math.floor(totalSeconds / 3600);
    const minutes = Math.floor((totalSeconds % 3600) / 60);
    const seconds = totalSeconds % 60;
    const pad = (value: number) => value.toString().padStart(2, '0');

    return hours > 0 ? `${pad(hours)}:${pad(minutes)}:${pad(seconds)}` : `${pad(minutes)}:${pad(seconds)}`;
  }

  initiateMercadoPago(): void {
    if (this.isSubmitting()) {
      return;
    }

    const reservationId = this.reservationId();

    if (!reservationId) {
      return;
    }

    this.actionState.set({ status: 'submittingMercadoPago' });

    this.paymentService
      .initiateMercadoPago(reservationId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (response) => {
          if (reservationId !== this.reservationId()) {
            return;
          }

          // Only the two correlation ids — never the checkoutUrl, tokens,
          // or provider order id — before leaving the app entirely.
          this.returnContextStore.save({
            paymentId: response.paymentId,
            reservationId
          });
          this.actionState.set({ status: 'idle' });
          this.externalNavigation.navigateTo(response.checkoutUrl);
        },
        error: (error: ApiError) => {
          if (reservationId !== this.reservationId()) {
            return;
          }

          this.actionState.set({ status: 'error', method: 'MercadoPago', error });
        }
      });
  }

  declareCash(): void {
    if (this.isSubmitting()) {
      return;
    }

    const reservationId = this.reservationId();

    if (!reservationId) {
      return;
    }

    this.actionState.set({ status: 'submittingCash' });

    this.paymentService
      .declareCash(reservationId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (response) => {
          if (reservationId !== this.reservationId()) {
            return;
          }

          this.actionState.set({ status: 'idle' });
          // Fetches the full read model immediately — the declare response
          // itself is intentionally minimal (paymentId/status/expiry only).
          this.cashPaymentIdSignal.set(response.paymentId);
        },
        error: (error: ApiError) => {
          if (reservationId !== this.reservationId()) {
            return;
          }

          this.actionState.set({ status: 'error', method: 'Cash', error });
        }
      });
  }
}
