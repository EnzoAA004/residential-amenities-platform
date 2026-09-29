import { Component, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { IonButton, IonNote, IonSpinner, IonText } from '@ionic/angular';
import { Subject, catchError, map, merge, of, startWith, switchMap } from 'rxjs';
import { toSignal } from '@angular/core/rxjs-interop';

import { ApiError } from '../../../core/api/api-error';
import { AppShellComponent } from '../../../layout/app-shell/app-shell.component';
import { MercadoPagoReturnContextStore } from './mercado-pago-return-context.store';
import { Payment } from './payment.models';
import { PaymentService } from './payment.service';

type ReturnState =
  | { status: 'noContext' }
  | { status: 'loading' }
  | { status: 'success'; payment: Payment }
  | { status: 'error'; error: ApiError }
  // The GET succeeded, but the payment it returned does not belong to the
  // reservation this session actually started paying for. This is treated
  // as a hard stop, never as a variant of `success` — no status is inferred
  // and the mismatched payment is never rendered.
  | { status: 'correlationError' };

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
 * Where the browser lands after Mercado Pago's Checkout Pro (issue #49).
 *
 * **This component never reads route query parameters, and never injects
 * `ActivatedRoute` at all.** Mercado Pago's `back_urls` are UX-only
 * configuration (RB-011) and the provider may append its own query
 * parameters on return — `status`, `payment_id`, `collection_status`,
 * `preference_id`, etc. — none of which this app can trust as identity or
 * proof of payment. The only paymentId this page will ever query is the one
 * `PaymentPage` saved to `MercadoPagoReturnContextStore` (`sessionStorage`)
 * right before redirecting to `checkoutUrl`. A visitor who lands here with
 * no such context (a different tab, a cleared session, a direct bookmark)
 * sees a neutral "no payment found" state — this page never guesses an id
 * from the URL.
 */
@Component({
  selector: 'app-payment-return-page',
  standalone: true,
  imports: [AppShellComponent, IonButton, IonNote, IonSpinner, IonText, RouterLink],
  styles: [
    `
      .payment-card {
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
    `
  ],
  template: `
    <app-shell title="Resultado del pago">
      @switch (returnState().status) {
        @case ('noContext') {
          <p>
            <ion-text color="medium">No encontramos un pago iniciado en esta sesión.</ion-text>
          </p>
          <ion-button routerLink="/amenities">Volver a amenities</ion-button>
        }
        @case ('loading') {
          <p aria-live="polite">
            <ion-spinner name="dots" /> <ion-text color="medium">Consultando el pago…</ion-text>
          </p>
        }
        @case ('error') {
          <p role="alert">
            <ion-text color="danger">{{ errorTitle() }}</ion-text>
            @if (errorDetail(); as detail) {
              <br />
              <ion-text color="danger">{{ detail }}</ion-text>
            }
          </p>
          <ion-button type="button" fill="clear" (click)="refresh()">Actualizar estado</ion-button>
        }
        @case ('correlationError') {
          <p role="alert">
            <ion-text color="danger">
              No pudimos correlacionar este pago con la reserva iniciada en esta sesión.
            </ion-text>
          </p>
          <ion-button routerLink="/amenities">Volver a amenities</ion-button>
        }
        @case ('success') {
          <section class="payment-card" aria-live="polite">
            <dl>
              <dt>Método</dt>
              <dd>{{ methodLabel(payment()!.method) }}</dd>
              <dt>Estado del pago</dt>
              <dd>{{ statusLabel(payment()!.status) }}</dd>
              <dt>Monto</dt>
              <dd>{{ formatAmount(payment()!.amount, payment()!.currency) }}</dd>
              @if (payment()!.approvedAtUtc; as approvedAt) {
                <dt>Aprobado</dt>
                <dd>{{ formatDateTime(approvedAt) }}</dd>
              }
            </dl>

            @if (payment()!.status === 'Created') {
              <p>
                <ion-text color="medium">
                  El intento de pago quedó registrado. Todavía puede no estar listo del lado del
                  proveedor.
                </ion-text>
              </p>
            } @else if (payment()!.status === 'Pending') {
              <p>
                <ion-text color="medium">El pago está pendiente de procesarse.</ion-text>
              </p>
            } @else if (payment()!.status === 'Approved' && payment()!.reservationOutcome === 'ReservationConfirmed') {
              <p>
                <ion-text color="success">Pago aprobado y reserva confirmada.</ion-text>
              </p>
            } @else if (payment()!.status === 'Approved' && payment()!.requiresManualReview) {
              <p role="alert">
                <ion-note color="warning">
                  El pago fue aprobado, pero requiere revisión manual.
                  {{ outcomeExplanation(payment()!.reservationOutcome) }}
                </ion-note>
              </p>
            } @else if (payment()!.status === 'Rejected') {
              <p>
                <ion-text color="danger">El pago fue rechazado.</ion-text>
              </p>
            } @else if (payment()!.status === 'Cancelled') {
              <p>
                <ion-text color="medium">El intento de pago fue cancelado.</ion-text>
              </p>
            }

            <ion-button type="button" fill="clear" (click)="refresh()">Actualizar estado</ion-button>
          </section>
        }
      }
    </app-shell>
  `
})
export class PaymentReturnPage {
  private readonly paymentService = inject(PaymentService);
  private readonly returnContextStore = inject(MercadoPagoReturnContextStore);

  // Read exactly once, at construction — never re-derived from the URL.
  private readonly context = this.returnContextStore.read();

  private readonly refreshSubject = new Subject<void>();

  readonly returnState = toSignal(
    this.context
      ? merge(of(null), this.refreshSubject).pipe(
          switchMap(() =>
            this.paymentService.getPayment(this.context!.paymentId).pipe(
              map((payment): ReturnState => {
                // Defense in depth: the URL already pins the request to
                // `context.paymentId`, but a mismatched `reservationId` (or,
                // in principle, a backend bug returning the wrong payment
                // for that id) must never be rendered as this session's
                // result — no status is inferred from a payment that isn't
                // provably the one this browser started.
                if (
                  payment.paymentId !== this.context!.paymentId ||
                  payment.reservationId !== this.context!.reservationId
                ) {
                  this.returnContextStore.clear();
                  return { status: 'correlationError' };
                }

                return { status: 'success', payment };
              }),
              catchError((error: ApiError) => of<ReturnState>({ status: 'error', error })),
              startWith<ReturnState>({ status: 'loading' })
            )
          )
        )
      : of<ReturnState>({ status: 'noContext' }),
    { initialValue: (this.context ? { status: 'loading' } : { status: 'noContext' }) as ReturnState }
  );

  readonly payment = computed(() => {
    const state = this.returnState();
    return state.status === 'success' ? state.payment : null;
  });

  readonly errorTitle = computed(() => {
    const state = this.returnState();
    return state.status === 'error' ? state.error.title : '';
  });

  readonly errorDetail = computed(() => {
    const state = this.returnState();
    return state.status === 'error' ? (state.error.detail ?? null) : null;
  });

  refresh(): void {
    this.refreshSubject.next();
  }

  methodLabel(method: Payment['method']): string {
    return method === 'MercadoPago' ? 'Mercado Pago' : 'Efectivo';
  }

  statusLabel(status: Payment['status']): string {
    return PAYMENT_STATUS_LABELS[status];
  }

  outcomeExplanation(outcome: Payment['reservationOutcome']): string {
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
}
