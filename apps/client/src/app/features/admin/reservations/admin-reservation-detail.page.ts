import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { IonButton, IonText } from '@ionic/angular';
import { catchError, map, of, startWith, switchMap } from 'rxjs';

import { ApiError } from '../../../core/api/api-error';
import { AppErrorStateComponent } from '../../../shared/error-state/app-error-state.component';
import { AdminReservationDetail } from '../admin.models';
import { AdminService } from '../admin.service';

type DetailState =
  | { status: 'loading' }
  | { status: 'success'; detail: AdminReservationDetail }
  | { status: 'error'; error: ApiError };

/**
 * Cancel/reschedule are both non-idempotent-looking-but-backend-idempotent
 * mutations (already-cancelled cancel returns 200; the backend is the final
 * authority on reschedule conflicts). Neither state machine has an
 * `'unknown'` branch: both endpoints are safe to show a real error for and
 * let the admin manually retry from the error state — there is no
 * network-failure ambiguity about whether a second identical POST would
 * duplicate an effect (cancel is idempotent; reschedule failures are always
 * rejections the backend can explain).
 */
type CancelState =
  | { status: 'idle' }
  | { status: 'submitting' }
  | { status: 'error'; error: ApiError };

type RescheduleState =
  | { status: 'idle' }
  | { status: 'submitting' }
  | { status: 'error'; error: ApiError };

function isReasonValid(reason: string): boolean {
  const trimmed = reason.trim();
  return trimmed.length > 0 && trimmed.length <= 500;
}

@Component({
  selector: 'app-admin-reservation-detail-page',
  standalone: true,
  imports: [AppErrorStateComponent, IonButton, IonText, ReactiveFormsModule, RouterLink],
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

      .op-form {
        display: grid;
        gap: var(--app-space-2);
        margin-top: var(--app-space-2);
      }

      label {
        display: grid;
        gap: var(--app-space-1);
        font-size: var(--app-font-size-sm);
      }

      textarea,
      input[type='date'],
      input[type='time'] {
        border: 1px solid var(--app-border-color);
        border-radius: var(--app-radius-sm);
        padding: var(--app-space-2);
        font: inherit;
      }

      .op-form__fields {
        display: grid;
        grid-template-columns: repeat(auto-fit, minmax(160px, 1fr));
        gap: var(--app-space-3);
      }

      .op-form__actions {
        display: flex;
        gap: var(--app-space-3);
        flex-wrap: wrap;
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
          <app-error-state [title]="errorTitle()" [detail]="errorDetail() ?? undefined" />
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

            <section class="detail-panel" aria-label="Cancelar reserva">
              <h2>Cancelar reserva</h2>
              <p>
                <ion-text color="medium">
                  Cancelar la reserva nunca modifica sus pagos: un pago ya aprobado no se cancela ni
                  se reembolsa automáticamente desde acá.
                </ion-text>
              </p>

              @if (!cancelFormOpen()) {
                <ion-button type="button" fill="outline" (click)="openCancelForm()">
                  Cancelar reserva
                </ion-button>
              } @else {
                <form class="op-form" [formGroup]="cancelForm" (ngSubmit)="confirmCancel()">
                  <label [attr.for]="cancelReasonId">
                    Motivo (obligatorio, máx. 500 caracteres)
                    <textarea [id]="cancelReasonId" formControlName="reason" rows="3"></textarea>
                  </label>

                  @if (cancelReasonError(); as error) {
                    <app-error-state [title]="error" />
                  }

                  <div class="op-form__actions">
                    <ion-button
                      type="submit"
                      color="danger"
                      [disabled]="cancelState().status === 'submitting' || !isCancelReasonValid()"
                    >
                      {{ cancelState().status === 'submitting' ? 'Cancelando…' : 'Confirmar cancelación' }}
                    </ion-button>
                    <ion-button
                      type="button"
                      fill="clear"
                      [disabled]="cancelState().status === 'submitting'"
                      (click)="closeCancelForm()"
                    >
                      Volver
                    </ion-button>
                  </div>
                </form>
              }

              @if (cancelState(); as cancel) {
                @if (cancel.status === 'error') {
                  <app-error-state [title]="cancel.error.title" [detail]="cancel.error.detail" />
                }
              }
            </section>

            <section class="detail-panel" aria-label="Reprogramar reserva">
              <h2>Reprogramar reserva</h2>
              <p>
                <ion-text color="medium">
                  Los horarios se ingresan en UTC explícitamente — esta pantalla no convierte ninguna
                  hora local.
                </ion-text>
              </p>

              @if (!rescheduleFormOpen()) {
                <ion-button type="button" fill="outline" (click)="openRescheduleForm()">
                  Reprogramar reserva
                </ion-button>
              } @else {
                <form class="op-form" [formGroup]="rescheduleForm" (ngSubmit)="confirmReschedule()">
                  <div class="op-form__fields">
                    <label [attr.for]="startDateId">
                      Fecha inicio (UTC)
                      <input [id]="startDateId" type="date" formControlName="startDate" />
                    </label>
                    <label [attr.for]="startTimeId">
                      Hora inicio (UTC)
                      <input [id]="startTimeId" type="time" formControlName="startTime" />
                    </label>
                    <label [attr.for]="endDateId">
                      Fecha fin (UTC)
                      <input [id]="endDateId" type="date" formControlName="endDate" />
                    </label>
                    <label [attr.for]="endTimeId">
                      Hora fin (UTC)
                      <input [id]="endTimeId" type="time" formControlName="endTime" />
                    </label>
                  </div>

                  <label [attr.for]="rescheduleReasonId">
                    Motivo (obligatorio, máx. 500 caracteres)
                    <textarea [id]="rescheduleReasonId" formControlName="reason" rows="3"></textarea>
                  </label>

                  @if (rescheduleValidationError(); as error) {
                    <app-error-state [title]="error" />
                  }

                  <div class="op-form__actions">
                    <ion-button
                      type="submit"
                      [disabled]="rescheduleState().status === 'submitting' || !isRescheduleFormValid()"
                    >
                      {{
                        rescheduleState().status === 'submitting'
                          ? 'Reprogramando…'
                          : 'Confirmar reprogramación'
                      }}
                    </ion-button>
                    <ion-button
                      type="button"
                      fill="clear"
                      [disabled]="rescheduleState().status === 'submitting'"
                      (click)="closeRescheduleForm()"
                    >
                      Volver
                    </ion-button>
                  </div>
                </form>
              }

              @if (rescheduleState(); as reschedule) {
                @if (reschedule.status === 'error') {
                  <app-error-state [title]="reschedule.error.title" [detail]="reschedule.error.detail" />
                }
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
  private readonly destroyRef = inject(DestroyRef);
  private static nextInstanceId = 0;
  private readonly instanceId = AdminReservationDetailPage.nextInstanceId++;

  readonly cancelReasonId = `admin-reservation-cancel-reason-${this.instanceId}`;
  readonly startDateId = `admin-reservation-start-date-${this.instanceId}`;
  readonly startTimeId = `admin-reservation-start-time-${this.instanceId}`;
  readonly endDateId = `admin-reservation-end-date-${this.instanceId}`;
  readonly endTimeId = `admin-reservation-end-time-${this.instanceId}`;
  readonly rescheduleReasonId = `admin-reservation-reschedule-reason-${this.instanceId}`;

  readonly state = computed(() => this.stateSignal());
  private readonly stateSignal = signal<DetailState>({ status: 'loading' });
  readonly detail = computed(() => {
    const state = this.stateSignal();
    return state.status === 'success' ? state.detail : null;
  });
  readonly errorTitle = computed(() => {
    const state = this.stateSignal();
    return state.status === 'error' ? state.error.title : '';
  });
  readonly errorDetail = computed(() => {
    const state = this.stateSignal();
    return state.status === 'error' ? (state.error.detail ?? null) : null;
  });

  // Captures the reservationId a mutation was fired for; only applied to
  // `stateSignal` if the route is still on that same reservation when the
  // response arrives (same contextVersion-guard pattern as #47/#48/#49).
  private currentReservationId = '';

  readonly cancelFormOpen = signal(false);
  readonly cancelState = signal<CancelState>({ status: 'idle' });
  readonly cancelForm = new FormGroup({
    reason: new FormControl('', { nonNullable: true })
  });

  readonly rescheduleFormOpen = signal(false);
  readonly rescheduleState = signal<RescheduleState>({ status: 'idle' });
  readonly rescheduleForm = new FormGroup({
    startDate: new FormControl('', { nonNullable: true }),
    startTime: new FormControl('', { nonNullable: true }),
    endDate: new FormControl('', { nonNullable: true }),
    endTime: new FormControl('', { nonNullable: true }),
    reason: new FormControl('', { nonNullable: true })
  });

  constructor() {
    this.route.paramMap
      .pipe(
        map((params) => params.get('id') ?? ''),
        switchMap((reservationId) => {
          this.currentReservationId = reservationId;
          this.resetOperationState();

          return this.admin.getReservation(reservationId).pipe(
            map((detail): DetailState => ({ status: 'success', detail })),
            catchError((error: ApiError) => of<DetailState>({ status: 'error', error })),
            startWith<DetailState>({ status: 'loading' })
          );
        })
      )
      .subscribe((state) => this.stateSignal.set(state));
  }

  private resetOperationState(): void {
    this.cancelFormOpen.set(false);
    this.cancelState.set({ status: 'idle' });
    this.cancelForm.reset({ reason: '' });
    this.rescheduleFormOpen.set(false);
    this.rescheduleState.set({ status: 'idle' });
    this.rescheduleForm.reset({
      startDate: '',
      startTime: '',
      endDate: '',
      endTime: '',
      reason: ''
    });
  }

  // --- Cancel -----------------------------------------------------------

  isCancelReasonValid(): boolean {
    return isReasonValid(this.cancelForm.controls.reason.value);
  }

  cancelReasonError(): string | null {
    const reason = this.cancelForm.controls.reason.value;

    if (reason.trim().length === 0) {
      return 'El motivo es obligatorio.';
    }

    if (reason.trim().length > 500) {
      return 'El motivo no puede superar los 500 caracteres.';
    }

    return null;
  }

  openCancelForm(): void {
    this.cancelState.set({ status: 'idle' });
    this.cancelFormOpen.set(true);
  }

  closeCancelForm(): void {
    this.cancelFormOpen.set(false);
    this.cancelState.set({ status: 'idle' });
    this.cancelForm.reset({ reason: '' });
  }

  confirmCancel(): void {
    // Guards both the explicit-confirmation requirement (the form must be
    // open, i.e. the admin already went through the confirm step) and
    // double-submit (a second click while 'submitting' is a no-op).
    if (!this.cancelFormOpen() || this.cancelState().status === 'submitting') {
      return;
    }

    if (!this.isCancelReasonValid()) {
      return;
    }

    const reservationId = this.currentReservationId;
    const reason = this.cancelForm.controls.reason.value.trim();

    this.cancelState.set({ status: 'submitting' });

    this.admin
      .cancelReservation(reservationId, { reason })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (detail) => {
          if (reservationId !== this.currentReservationId) {
            return;
          }

          this.stateSignal.set({ status: 'success', detail });
          this.cancelState.set({ status: 'idle' });
          this.cancelFormOpen.set(false);
          this.cancelForm.reset({ reason: '' });
        },
        error: (error: ApiError) => {
          if (reservationId !== this.currentReservationId) {
            return;
          }

          this.cancelState.set({ status: 'error', error });
        }
      });
  }

  // --- Reschedule ---------------------------------------------------------

  private rescheduleIso(datePart: string, timePart: string): string | null {
    if (!datePart || !timePart) {
      return null;
    }

    // Built directly from the (already-UTC-labeled) date/time input strings
    // — never through `new Date(localString)`, which would silently apply
    // the browser's local offset. `input[type=date|time]` values are plain
    // `YYYY-MM-DD` / `HH:mm` strings with no timezone of their own, so
    // appending `Z` is the only correct way to make this an explicit UTC
    // instant.
    return `${datePart}T${timePart}:00.000Z`;
  }

  private rescheduleRequestOrNull(): { startsAtUtc: string; endsAtUtc: string; reason: string } | null {
    const value = this.rescheduleForm.getRawValue();
    const startsAtUtc = this.rescheduleIso(value.startDate, value.startTime);
    const endsAtUtc = this.rescheduleIso(value.endDate, value.endTime);

    if (!startsAtUtc || !endsAtUtc) {
      return null;
    }

    return { startsAtUtc, endsAtUtc, reason: value.reason.trim() };
  }

  rescheduleValidationError(): string | null {
    const value = this.rescheduleForm.getRawValue();

    if (value.reason.trim().length === 0) {
      return 'El motivo es obligatorio.';
    }

    if (value.reason.trim().length > 500) {
      return 'El motivo no puede superar los 500 caracteres.';
    }

    if (!value.startDate || !value.startTime) {
      return 'La fecha y hora de inicio (UTC) son obligatorias.';
    }

    if (!value.endDate || !value.endTime) {
      return 'La fecha y hora de fin (UTC) son obligatorias.';
    }

    const request = this.rescheduleRequestOrNull();

    if (!request) {
      return 'La fecha y hora (UTC) son obligatorias.';
    }

    if (new Date(request.endsAtUtc).getTime() <= new Date(request.startsAtUtc).getTime()) {
      return 'El fin debe ser posterior al inicio.';
    }

    return null;
  }

  isRescheduleFormValid(): boolean {
    return this.rescheduleValidationError() === null;
  }

  openRescheduleForm(): void {
    this.rescheduleState.set({ status: 'idle' });
    this.rescheduleFormOpen.set(true);
  }

  closeRescheduleForm(): void {
    this.rescheduleFormOpen.set(false);
    this.rescheduleState.set({ status: 'idle' });
    this.rescheduleForm.reset({
      startDate: '',
      startTime: '',
      endDate: '',
      endTime: '',
      reason: ''
    });
  }

  confirmReschedule(): void {
    if (!this.rescheduleFormOpen() || this.rescheduleState().status === 'submitting') {
      return;
    }

    if (!this.isRescheduleFormValid()) {
      return;
    }

    const request = this.rescheduleRequestOrNull();

    if (!request) {
      return;
    }

    const reservationId = this.currentReservationId;

    this.rescheduleState.set({ status: 'submitting' });

    this.admin
      .rescheduleReservation(reservationId, request)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (detail) => {
          if (reservationId !== this.currentReservationId) {
            return;
          }

          this.stateSignal.set({ status: 'success', detail });
          this.rescheduleState.set({ status: 'idle' });
          this.rescheduleFormOpen.set(false);
          this.rescheduleForm.reset({
            startDate: '',
            startTime: '',
            endDate: '',
            endTime: '',
            reason: ''
          });
        },
        error: (error: ApiError) => {
          if (reservationId !== this.currentReservationId) {
            return;
          }

          this.rescheduleState.set({ status: 'error', error });
        }
      });
  }
}
