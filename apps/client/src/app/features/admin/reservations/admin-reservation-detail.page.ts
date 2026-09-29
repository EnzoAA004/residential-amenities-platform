import { Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { IonButton, IonText } from '@ionic/angular';
import { catchError, map, of, startWith, switchMap } from 'rxjs';

import { ApiError } from '../../../core/api/api-error';
import { AdminReservationDetail } from '../admin.models';
import { AdminService } from '../admin.service';

type DetailState =
  | { status: 'loading' }
  | { status: 'success'; detail: AdminReservationDetail }
  | { status: 'error'; error: ApiError };

@Component({
  selector: 'app-admin-reservation-detail-page',
  standalone: true,
  imports: [IonButton, IonText, RouterLink],
  styles: [
    `
      .detail-grid,
      .detail-list {
        display: grid;
        gap: var(--app-space-3);
      }

      .detail-panel {
        border: 1px solid var(--app-border-color);
        border-radius: var(--app-radius-md);
        padding: var(--app-space-3);
        background: var(--app-surface);
      }
    `
  ],
  template: `
    <section aria-labelledby="admin-reservation-detail-title">
      <ion-button routerLink="/admin/reservations" fill="clear">Volver</ion-button>

      @switch (state().status) {
        @case ('loading') {
          <p><ion-text color="medium">Cargando reserva…</ion-text></p>
        }
        @case ('error') {
          <p role="alert"><ion-text color="danger">{{ errorTitle() }}</ion-text></p>
        }
        @case ('success') {
          <div class="detail-grid">
            <div>
              <h1 id="admin-reservation-detail-title">
                {{ detail()!.reservation.useType }} · {{ detail()!.reservation.status }}
              </h1>
              @if (detail()!.requiresFinancialReview) {
                <p role="alert">
                  <ion-text color="warning">Esta reserva requiere revisión financiera.</ion-text>
                </p>
              }
            </div>

            <section class="detail-panel" aria-label="Lifecycle">
              <h2>Lifecycle</h2>
              <p>Creada: {{ detail()!.reservation.createdAtUtc }}</p>
              <p>Inicio: {{ detail()!.reservation.startsAtUtc }}</p>
              <p>Fin: {{ detail()!.reservation.endsAtUtc }}</p>
              <p>Expira: {{ detail()!.reservation.expiresAtUtc }}</p>
              <p>Confirmada: {{ detail()!.reservation.confirmedAtUtc ?? 'No' }}</p>
              <p>Cancelada: {{ detail()!.reservation.cancelledAtUtc ?? 'No' }}</p>
              <p>Vencida: {{ detail()!.reservation.expiredAtUtc ?? 'No' }}</p>
              @if (detail()!.reservation.cancellationReason) {
                <p>Motivo: {{ detail()!.reservation.cancellationReason }}</p>
              }
            </section>

            <section class="detail-panel" aria-label="Resources">
              <h2>Resources</h2>
              <ul>
                @for (resource of detail()!.reservation.resources; track resource.amenityId) {
                  <li>{{ resource.amenityId }} · {{ resource.isExclusive ? 'Exclusive' : 'Shared' }}</li>
                }
              </ul>
            </section>

            <section class="detail-panel" aria-label="Price snapshot">
              <h2>Price snapshot</h2>
              <p>Total: {{ detail()!.reservation.total }} {{ detail()!.reservation.currency ?? '' }}</p>
              <ul>
                @for (line of detail()!.priceLines; track line.amenityId + line.componentType) {
                  <li>{{ line.componentType }} · {{ line.amount }} {{ line.currency }} · {{ line.amenityId }}</li>
                }
              </ul>
            </section>

            <section class="detail-panel" aria-label="Payments">
              <h2>Payments</h2>
              @if (detail()!.payments.length === 0) {
                <p><ion-text color="medium">Sin pagos registrados.</ion-text></p>
              } @else {
                <ul class="detail-list">
                  @for (payment of detail()!.payments; track payment.paymentId) {
                    <li>
                      <strong>{{ payment.method }} · {{ payment.status }}</strong>
                      <p>{{ payment.amount }} {{ payment.currency }} · {{ payment.createdAtUtc }}</p>
                      <p>Outcome: {{ payment.reservationOutcome }}</p>
                      @if (payment.approvedAtUtc) {
                        <p>Aprobado: {{ payment.approvedAtUtc }}</p>
                      }
                      @if (payment.cashConfirmedAtUtc) {
                        <p>Efectivo confirmado: {{ payment.cashConfirmedAtUtc }}</p>
                      }
                      @if (payment.requiresManualReview) {
                        <p><ion-text color="warning">Este pago requiere revisión administrativa.</ion-text></p>
                      }
                    </li>
                  }
                </ul>
              }
            </section>
          </div>
        }
      }
    </section>
  `
})
export class AdminReservationDetailPage {
  private readonly route = inject(ActivatedRoute);
  private readonly admin = inject(AdminService);

  protected readonly state = computed(() => this.stateSignal());
  private readonly stateSignal = signal<DetailState>({ status: 'loading' });
  protected readonly detail = computed(() => {
    const state = this.stateSignal();
    return state.status === 'success' ? state.detail : null;
  });
  protected readonly errorTitle = computed(() => {
    const state = this.stateSignal();
    return state.status === 'error' ? state.error.title : '';
  });

  constructor() {
    this.route.paramMap
      .pipe(
        map((params) => params.get('id') ?? ''),
        switchMap((reservationId) =>
          this.admin.getReservation(reservationId).pipe(
            map((detail): DetailState => ({ status: 'success', detail })),
            catchError((error: ApiError) => of<DetailState>({ status: 'error', error })),
            startWith<DetailState>({ status: 'loading' })
          )
        )
      )
      .subscribe((state) => this.stateSignal.set(state));
  }
}
