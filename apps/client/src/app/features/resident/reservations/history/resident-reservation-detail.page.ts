import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { IonButton, IonNote, IonSpinner, IonText } from '@ionic/angular';
import { Subject, catchError, distinctUntilChanged, forkJoin, map, merge, of, startWith, switchMap } from 'rxjs';

import { ApiError } from '../../../../core/api/api-error';
import { AppShellComponent } from '../../../../layout/app-shell/app-shell.component';
import { Reservation } from '../reservation.models';
import { ResidentReservationPayment } from './resident-reservation.models';
import { ResidentReservationService } from './resident-reservation.service';

type ReservationDetailState =
  | { status: 'loading' }
  | { status: 'success'; reservation: Reservation; payments: ResidentReservationPayment[] }
  | { status: 'error'; error: ApiError };

const OUTCOME_EXPLANATIONS: Record<ResidentReservationPayment['reservationOutcome'], string> = {
  None: '',
  ReservationConfirmed: 'La reserva fue confirmada.',
  ApprovedAfterExpiry: 'El pago se acreditó después de que venciera el plazo de la reserva.',
  ApprovedForCancelledReservation: 'El pago se acreditó pero la reserva ya estaba cancelada.',
  ApprovedForMissingReservation: 'El pago se acreditó pero no se encontró la reserva asociada.'
};

@Component({
  selector: 'app-resident-reservation-detail-page',
  standalone: true,
  imports: [AppShellComponent, RouterLink, IonButton, IonNote, IonSpinner, IonText],
  styles: [
    `
      .detail-card,
      .payment-card {
        border: 1px solid var(--app-border-color);
        border-radius: var(--app-radius-md);
        padding: var(--app-space-3);
        margin-block: var(--app-space-3);
      }

      dl {
        display: grid;
        grid-template-columns: auto 1fr;
        gap: var(--app-space-1) var(--app-space-3);
        margin: 0;
      }

      dt {
        font-weight: 600;
      }

      dd {
        margin: 0;
      }

      .payment-list {
        display: grid;
        gap: var(--app-space-3);
        list-style: none;
        padding: 0;
      }
    `
  ],
  template: `
    <app-shell title="Detalle de reserva">
      <ion-button type="button" fill="clear" routerLink="/reservations">Volver a mis reservas</ion-button>

      @switch (detailState().status) {
        @case ('loading') {
          <p aria-live="polite">
            <ion-spinner name="dots" /> <ion-text color="medium">Cargando reserva…</ion-text>
          </p>
        }
        @case ('error') {
          <p role="alert">
            <ion-text color="danger">{{ detailErrorTitle() }}</ion-text>
            @if (detailErrorDetail(); as detail) {
              <br />
              <ion-text color="danger">{{ detail }}</ion-text>
            }
          </p>
          <ion-button type="button" fill="outline" (click)="reloadDetail()">Retry</ion-button>
        }
        @case ('success') {
          @if (reservation(); as reservation) {
            <section class="detail-card">
              <h2>{{ reservation.useType }}</h2>
              <dl>
                <dt>Estado</dt>
                <dd>{{ reservation.status }}</dd>
                <dt>Inicio</dt>
                <dd>{{ formatDateTime(reservation.startsAtUtc) }}</dd>
                <dt>Fin</dt>
                <dd>{{ formatDateTime(reservation.endsAtUtc) }}</dd>
                <dt>Creada</dt>
                <dd>{{ formatDateTime(reservation.createdAtUtc) }}</dd>
                <dt>Total</dt>
                <dd>{{ formatAmount(reservation.totalAmount, reservation.currency) }}</dd>
              </dl>

              @if (reservation.status === 'Pending') {
                @let remaining = remainingMs();
                <p>
                  @if (remaining !== null) {
                    <ion-text [color]="remaining > 0 ? 'medium' : 'warning'">
                      @if (remaining > 0) {
                        Tiempo restante del hold: {{ formatRemaining(remaining) }}
                      } @else {
                        El plazo indicado llegó a cero; el servidor sigue siendo la autoridad del estado.
                      }
                    </ion-text>
                  }
                </p>
              }

              @if (reservation.status === 'Cancelled' && reservation.cancellationReason) {
                <p>
                  <ion-note color="medium">Motivo: {{ reservation.cancellationReason }}</ion-note>
                </p>
              }
            </section>

            <section class="detail-card">
              <h3>Recursos</h3>
              <ul>
                @for (resource of reservation.resources; track resource.amenityId) {
                  <li>{{ resource.amenityId }} · {{ resource.isExclusive ? 'Exclusivo' : 'Compartido' }}</li>
                }
              </ul>
            </section>

            <section class="detail-card">
              <h3>Snapshot de precio</h3>
              <dl>
                @for (line of reservation.priceLines; track line.amenityId + line.componentType) {
                  <dt>{{ line.componentType }}</dt>
                  <dd>{{ line.amenityId }} · {{ formatAmount(line.amount, line.currency) }}</dd>
                }
              </dl>
            </section>

            <section class="detail-card">
              <h3>Historial de pagos</h3>
              @if (payments().length === 0) {
                <p>
                  <ion-text color="medium">Esta reserva todavía no tiene intentos de pago.</ion-text>
                </p>
              } @else {
                <ul class="payment-list" role="list">
                  @for (payment of payments(); track payment.paymentId) {
                    <li class="payment-card">
                      <dl>
                        <dt>Método</dt>
                        <dd>{{ payment.method }}</dd>
                        <dt>Estado</dt>
                        <dd>{{ payment.status }}</dd>
                        <dt>Monto</dt>
                        <dd>{{ formatAmount(payment.amount, payment.currency) }}</dd>
                        <dt>Creado</dt>
                        <dd>{{ formatDateTime(payment.createdAtUtc) }}</dd>
                        <dt>Aprobado</dt>
                        <dd>{{ payment.approvedAtUtc ? formatDateTime(payment.approvedAtUtc) : '—' }}</dd>
                        <dt>Resultado reserva</dt>
                        <dd>{{ payment.reservationOutcome }}</dd>
                        <dt>Efectivo confirmado</dt>
                        <dd>{{ payment.cashConfirmedAtUtc ? formatDateTime(payment.cashConfirmedAtUtc) : '—' }}</dd>
                      </dl>

                      @if (payment.requiresManualReview) {
                        <p role="alert">
                          <ion-note color="warning">
                            Este pago requiere revisión administrativa.
                            {{ outcomeExplanation(payment.reservationOutcome) }}
                          </ion-note>
                        </p>
                      }
                    </li>
                  }
                </ul>
              }
            </section>
          }
        }
      }
    </app-shell>
  `
})
export class ResidentReservationDetailPage {
  private readonly route = inject(ActivatedRoute);
  private readonly reservations = inject(ResidentReservationService);
  private readonly destroyRef = inject(DestroyRef);

  private readonly reservationIdChanges$ = this.route.paramMap.pipe(
    map((params) => params.get('reservationId') ?? ''),
    distinctUntilChanged()
  );

  readonly reservationId = toSignal(this.reservationIdChanges$, { initialValue: '' });
  private readonly retrySubject = new Subject<void>();
  private readonly nowMs = signal(Date.now());

  readonly detailState = toSignal(
    merge(
      this.reservationIdChanges$,
      this.retrySubject.pipe(map(() => this.reservationId()))
    ).pipe(
      switchMap((reservationId) => {
        if (!reservationId) {
          return of<ReservationDetailState>({
            status: 'error',
            error: { status: 400, title: 'Falta el identificador de la reserva.' }
          });
        }

        return forkJoin({
          reservation: this.reservations.getById(reservationId),
          payments: this.reservations.getPayments(reservationId)
        }).pipe(
          map(({ reservation, payments }): ReservationDetailState => ({
            status: 'success',
            reservation,
            payments
          })),
          catchError((error: ApiError) => of<ReservationDetailState>({ status: 'error', error })),
          startWith<ReservationDetailState>({ status: 'loading' })
        );
      })
    ),
    { initialValue: { status: 'loading' } as ReservationDetailState }
  );

  readonly reservation = computed(() => {
    const state = this.detailState();
    return state.status === 'success' ? state.reservation : null;
  });

  readonly payments = computed(() => {
    const state = this.detailState();
    return state.status === 'success' ? state.payments : [];
  });

  readonly detailErrorTitle = computed(() => {
    const state = this.detailState();
    return state.status === 'error' ? state.error.title : '';
  });

  readonly detailErrorDetail = computed(() => {
    const state = this.detailState();
    return state.status === 'error' ? (state.error.detail ?? null) : null;
  });

  readonly remainingMs = computed<number | null>(() => {
    const reservation = this.reservation();
    if (!reservation) {
      return null;
    }

    return Math.max(0, new Date(reservation.expiresAtUtc).getTime() - this.nowMs());
  });

  constructor() {
    const timer = setInterval(() => this.nowMs.set(Date.now()), 1000);
    this.destroyRef.onDestroy(() => clearInterval(timer));
  }

  reloadDetail(): void {
    this.retrySubject.next();
  }

  outcomeExplanation(outcome: ResidentReservationPayment['reservationOutcome']): string {
    return OUTCOME_EXPLANATIONS[outcome];
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
}
