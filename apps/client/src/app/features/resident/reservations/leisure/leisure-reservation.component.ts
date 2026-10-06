import { Component, DestroyRef, Input, computed, effect, inject, signal, untracked } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { IonButton, IonNote, IonSpinner, IonText } from '@ionic/angular';

import { ApiError } from '../../../../core/api/api-error';
import { ResidentContextStore } from '../../../../core/resident-context/resident-context.store';
import { AppErrorStateComponent } from '../../../../shared/error-state/app-error-state.component';
import { AmenitySummary } from '../../amenities/amenities.models';
import {
  defaultAvailabilityRange,
  toDateTimeLocalInputValue,
  validateAvailabilityRange
} from '../../amenities/availability-range';
import { PriceQuote, Reservation } from '../reservation.models';
import { LeisureReservationService } from './leisure-reservation.service';
import { LeisureUseType, SharedOccupancy } from './leisure-reservation.models';

type QuoteState =
  | { status: 'idle' }
  | { status: 'loading' }
  | { status: 'success'; quote: PriceQuote }
  | { status: 'error'; error: ApiError };

type OccupancyState =
  | { status: 'idle' }
  | { status: 'loading' }
  | { status: 'success'; unitLabels: string[] }
  | { status: 'error'; error: ApiError };

type CreateState =
  | { status: 'idle' }
  | { status: 'creating' }
  | { status: 'created'; reservation: Reservation; quotedAt: PriceQuote | null }
  | { status: 'error'; error: ApiError }
  | { status: 'unknown' };

const USE_TYPE_LABELS: Record<LeisureUseType, string> = {
  SharedLeisure: 'Shared',
  ExclusiveLeisure: 'Exclusive'
};

/**
 * The Leisure (Shared/Exclusive) reservation flow (issue #47): quote →
 * resident confirms → create. Ends when a `Pending` hold exists; payment
 * (#49) and Event (#48) are out of scope.
 *
 * `buildingId` is never an editable field — it always comes from
 * `ResidentContextStore.activeMembership()`, the same source #46 already
 * uses. The parent only renders this component for an amenity drawn from
 * the loaded amenities list, so `amenityId` is never free text either.
 */
@Component({
  selector: 'app-leisure-reservation',
  standalone: true,
  imports: [AppErrorStateComponent, ReactiveFormsModule, RouterLink, IonButton, IonNote, IonSpinner, IonText],
  styles: [
    `
      .leisure-form {
        display: grid;
        gap: var(--app-space-3);
        margin-block: var(--app-space-3);
      }

      .leisure-form__fields {
        display: grid;
        grid-template-columns: repeat(auto-fit, minmax(200px, 1fr));
        gap: var(--app-space-3);
      }

      label {
        display: grid;
        gap: var(--app-space-1);
        font-size: var(--app-font-size-sm);
      }

      input[type='datetime-local'] {
        border: 1px solid var(--app-border-color);
        border-radius: var(--app-radius-sm);
        padding: var(--app-space-2);
        font: inherit;
      }

      .use-type-options {
        display: flex;
        gap: var(--app-space-3);
        flex-wrap: wrap;
      }

      .use-type-option {
        display: inline-flex;
        align-items: center;
        gap: var(--app-space-1);
      }

      .quote-card,
      .confirmation-card {
        border: 1px solid var(--app-border-color);
        border-radius: var(--app-radius-md);
        padding: var(--app-space-3);
        margin-block: var(--app-space-3);
      }

      .confirmation-card dl {
        display: grid;
        grid-template-columns: auto 1fr;
        gap: var(--app-space-1) var(--app-space-3);
        margin: 0;
      }

      .confirmation-card dt {
        font-weight: 600;
      }

      .confirmation-card dd {
        margin: 0;
      }
    `
  ],
  template: `
    @if (createState(); as createStatus) {
      @if (createStatus.status === 'created') {
        <section class="confirmation-card" aria-live="polite">
          <h3>Reservation hold created</h3>
          <dl>
            <dt>Amenity</dt>
            <dd>{{ amenityName() }}</dd>
            <dt>Use type</dt>
            <dd>{{ useTypeLabel(createStatus.reservation.useType) }}</dd>
            <dt>From</dt>
            <dd>{{ formatDateTime(createStatus.reservation.startsAtUtc) }}</dd>
            <dt>To</dt>
            <dd>{{ formatDateTime(createStatus.reservation.endsAtUtc) }}</dd>
            <dt>Estado</dt>
            <dd>{{ createStatus.reservation.status }}</dd>
            <dt>Total</dt>
            <dd>{{ formatAmount(createStatus.reservation.totalAmount, createStatus.reservation.currency) }}</dd>
            <dt>Hold expires</dt>
            <dd>{{ formatDateTime(createStatus.reservation.expiresAtUtc) }}</dd>
          </dl>

          @if (priceChanged(createStatus)) {
            <p>
              <ion-note color="warning">
                El precio se actualizó al crear la reserva. Este es el importe registrado en el hold.
              </ion-note>
            </p>
          }

          @if (createStatus.reservation.status === 'Confirmed') {
            <p>
              <ion-text color="success">Esta reserva es gratuita y ya está confirmada. No se requiere ningún pago.</ion-text>
            </p>
          } @else {
            <p>
              <ion-text color="medium">El pago se realizará en el siguiente paso.</ion-text>
            </p>

            <ion-button [routerLink]="['/reservations', createStatus.reservation.id, 'payment']">
              Continuar al pago
            </ion-button>
          }

          <ion-button type="button" fill="outline" (click)="resetFlow()">
            Create another reservation
          </ion-button>
        </section>
      } @else {
        @if (eligibleUseTypes().length === 0) {
          <p>
            <ion-text color="medium">Esta amenity no admite reservas de ocio.</ion-text>
          </p>
        } @else {
          <form class="leisure-form" [formGroup]="form" (ngSubmit)="requestQuote()">
            @if (eligibleUseTypes().length > 1) {
              <fieldset>
                <legend>Use type</legend>
                <div class="use-type-options">
                  @for (option of eligibleUseTypes(); track option) {
                    <label class="use-type-option">
                      <input
                        type="radio"
                        name="leisure-use-type"
                        [value]="option"
                        [checked]="form.controls.useType.value === option"
                        (change)="selectUseType(option)"
                      />
                      {{ useTypeLabel(option) }}
                    </label>
                  }
                </div>
                @if (useTypeError(); as error) {
                  <ion-note color="danger" role="alert">{{ error }}</ion-note>
                }
              </fieldset>
            } @else {
              <p>
                <ion-text color="medium">Uso: {{ useTypeLabel(eligibleUseTypes()[0]) }}</ion-text>
              </p>
            }

            <div class="leisure-form__fields">
              <label [attr.for]="fromInputId">
                From
                <input [id]="fromInputId" type="datetime-local" formControlName="from" />
              </label>

              <label [attr.for]="toInputId">
                To
                <input [id]="toInputId" type="datetime-local" formControlName="to" />
              </label>
            </div>

            @if (rangeError(); as error) {
              <ion-note color="danger" role="alert">{{ error }}</ion-note>
            }

            <ion-button type="submit" fill="outline" [disabled]="quoteState().status === 'loading'">
              {{ quoteState().status === 'loading' ? 'Cotizando…' : 'Cotizar' }}
            </ion-button>
          </form>

          @switch (quoteState().status) {
            @case ('loading') {
              <p aria-live="polite">
                <ion-spinner name="dots" /> <ion-text color="medium">Cotizando…</ion-text>
              </p>
            }
            @case ('error') {
              <app-error-state
                [title]="quoteErrorTitle()"
                [detail]="quoteErrorDetail() ?? undefined"
                [showRetry]="true"
                retryLabel="Reintentar cotización"
                (retry)="requestQuote()"
              />
            }
            @case ('success') {
              <section class="quote-card" aria-live="polite">
                <p>
                  <strong>{{ formatAmount(quotedAmount(), quotedCurrency()) }}</strong>
                </p>
                <ion-text color="medium">
                  Este precio no queda garantizado hasta que se confirme la reserva.
                </ion-text>
              </section>
            }
          }

          @if (occupancyState(); as occupancy) {
            @if (occupancy.status === 'loading') {
              <p aria-live="polite">
                <ion-spinner name="dots" /> <ion-text color="medium">Consultando ocupación…</ion-text>
              </p>
            }
            @if (occupancy.status === 'success') {
              <section class="occupancy-notice" aria-live="polite">
                @if (occupancy.unitLabels.length > 0) {
                  <ion-text color="medium">
                    Este espacio ya está reservado por: {{ occupancy.unitLabels.join(', ') }}.
                  </ion-text>
                } @else {
                  <ion-text color="medium">
                    Todavía nadie más reservó este espacio para este período.
                  </ion-text>
                }
              </section>
            }
          }

          @if (quoteState().status === 'success') {
            <ion-button
              type="button"
              (click)="create()"
              [disabled]="createStatus.status === 'creating' || createStatus.status === 'unknown'"
            >
              {{ createStatus.status === 'creating' ? 'Creando…' : 'Confirmar reserva' }}
            </ion-button>
          }

          @if (createStatus.status === 'error') {
            <app-error-state [title]="createErrorTitle()" [detail]="createErrorDetail() ?? undefined" />
          }

          @if (createStatus.status === 'unknown') {
            <p role="alert">
              <ion-note color="warning">
                No pudimos confirmar el resultado de la creación. Evitá repetir inmediatamente la operación.
              </ion-note>
            </p>
          }
        }
      }
    }
  `
})
export class LeisureReservationComponent {
  private readonly leisureService = inject(LeisureReservationService);
  private readonly residentContext = inject(ResidentContextStore);
  private readonly destroyRef = inject(DestroyRef);
  private static nextInstanceId = 0;
  private readonly instanceId = LeisureReservationComponent.nextInstanceId++;

  readonly fromInputId = `leisure-reservation-from-${this.instanceId}`;
  readonly toInputId = `leisure-reservation-to-${this.instanceId}`;

  private readonly amenitySignal = signal<AmenitySummary | null>(null);
  private readonly preferredUseTypeSignal = signal<LeisureUseType | null>(null);
  private currentAmenityId: string | null = null;

  /**
   * Bumped whenever the request context (amenity or use type) changes.
   * `LeisureReservationComponent` is not destroyed when the resident picks
   * a different amenity under the same building — Angular reuses this same
   * instance and just rebinds `[amenity]` — so a quote/create request
   * already in flight for the *previous* amenity/use type can still
   * resolve after the switch. `takeUntilDestroyed` alone does not help
   * here (the component is still alive); each request instead captures the
   * version it started with and its `next`/`error` handler discards the
   * result if the version has since moved on, so a stale response can
   * never be shown under a context it does not belong to.
   */
  private contextVersion = 0;

  readonly eligibleUseTypes = computed<LeisureUseType[]>(() => {
    const amenity = this.amenitySignal();

    if (!amenity) {
      return [];
    }

    const options: LeisureUseType[] = [];

    if (amenity.allowsSharedUse) {
      options.push('SharedLeisure');
    }

    if (amenity.allowsExclusiveUse) {
      options.push('ExclusiveLeisure');
    }

    return options;
  });

  readonly amenityName = computed(() => this.amenitySignal()?.name ?? '');

  readonly form = new FormGroup({
    useType: new FormControl<LeisureUseType | null>(null),
    from: new FormControl(toDateTimeLocalInputValue(defaultAvailabilityRange().fromUtc), {
      nonNullable: true
    }),
    to: new FormControl(toDateTimeLocalInputValue(defaultAvailabilityRange().toUtc), {
      nonNullable: true
    })
  });

  readonly rangeError = signal<string | null>(null);
  readonly useTypeError = signal<string | null>(null);
  readonly quoteState = signal<QuoteState>({ status: 'idle' });
  readonly occupancyState = signal<OccupancyState>({ status: 'idle' });
  readonly createState = signal<CreateState>({ status: 'idle' });

  private readonly dateTimeFormatter = new Intl.DateTimeFormat(undefined, {
    dateStyle: 'medium',
    timeStyle: 'short'
  });

  constructor() {
    // A single eligible use type auto-selects (#46/#47 rule: "solo Shared"
    // / "solo Exclusive" never show an unnecessary selector); with two, the
    // resident always chooses explicitly, and with none the form does not
    // render at all (see the template).
    effect(() => {
      const options = this.eligibleUseTypes();

      const preferred = this.preferredUseTypeSignal();

      untracked(() => {
        this.form.controls.useType.setValue(
          preferred && options.includes(preferred)
            ? preferred
            : options.length === 1
              ? options[0]
              : null
        );
      });
    });

    // A quote is only valid for the use type it was requested for — any
    // change invalidates it, so the resident can never confirm a stale
    // amount against a different use type.
    this.form.controls.useType.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => {
      this.contextVersion++;

      if (this.createState().status !== 'created') {
        this.quoteState.set({ status: 'idle' });
      }
    });
  }

  @Input({ required: true })
  set amenity(value: AmenitySummary) {
    if (this.currentAmenityId === value.id) {
      return;
    }

    this.currentAmenityId = value.id;
    this.contextVersion++;
    this.amenitySignal.set(value);
    this.quoteState.set({ status: 'idle' });
    this.createState.set({ status: 'idle' });
    this.rangeError.set(null);
    this.useTypeError.set(null);
  }

  @Input()
  set preferredUseType(value: LeisureUseType | null | undefined) {
    this.preferredUseTypeSignal.set(value ?? null);
  }

  useTypeLabel(useType: string): string {
    return USE_TYPE_LABELS[useType as LeisureUseType] ?? useType;
  }

  selectUseType(useType: LeisureUseType): void {
    this.form.controls.useType.setValue(useType);
  }

  formatAmount(amount: number, currency: string): string {
    try {
      return new Intl.NumberFormat(undefined, { style: 'currency', currency }).format(amount);
    } catch {
      return `${amount} ${currency}`;
    }
  }

  formatDateTime(isoValue: string): string {
    return this.dateTimeFormatter.format(new Date(isoValue));
  }

  quotedAmount(): number {
    const state = this.quoteState();
    return state.status === 'success' ? state.quote.totalAmount : 0;
  }

  quotedCurrency(): string {
    const state = this.quoteState();
    return state.status === 'success' ? state.quote.currency : '';
  }

  quoteErrorTitle(): string {
    const state = this.quoteState();
    return state.status === 'error' ? state.error.title : '';
  }

  quoteErrorDetail(): string | null {
    const state = this.quoteState();
    return state.status === 'error' ? (state.error.detail ?? null) : null;
  }

  createErrorTitle(): string {
    const state = this.createState();
    return state.status === 'error' ? state.error.title : '';
  }

  createErrorDetail(): string | null {
    const state = this.createState();
    return state.status === 'error' ? (state.error.detail ?? null) : null;
  }

  priceChanged(state: { reservation: Reservation; quotedAt: PriceQuote | null }): boolean {
    const quoted = state.quotedAt;
    return (
      quoted !== null &&
      (quoted.totalAmount !== state.reservation.totalAmount || quoted.currency !== state.reservation.currency)
    );
  }

  requestQuote(): void {
    const buildingId = this.residentContext.activeBuildingId();
    const amenity = this.amenitySignal();
    const useType = this.form.controls.useType.value;

    if (!buildingId || !amenity) {
      return;
    }

    if (!useType) {
      this.useTypeError.set('Elegí un tipo de uso.');
      return;
    }

    this.useTypeError.set(null);

    const validation = validateAvailabilityRange(this.fromDate(), this.toDate());

    if (!validation.valid) {
      this.rangeError.set(validation.reason);
      return;
    }

    this.rangeError.set(null);
    this.quoteState.set({ status: 'loading' });
    this.occupancyState.set({ status: 'idle' });

    const requestVersion = this.contextVersion;

    this.leisureService
      .getQuote(buildingId, amenity.id, useType)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (quote) => {
          // The amenity or use type changed while this quote was in
          // flight — it no longer describes the current context, so it
          // must never appear as if it did.
          if (requestVersion === this.contextVersion) {
            this.quoteState.set({ status: 'success', quote });

            if (useType === 'SharedLeisure') {
              this.fetchSharedOccupancy(buildingId, amenity.id, requestVersion);
            }
          }
        },
        error: (error: ApiError) => {
          if (requestVersion === this.contextVersion) {
            this.quoteState.set({ status: 'error', error });
          }
        }
      });
  }

  /**
   * Issue #89: before the resident can confirm a SharedLeisure booking,
   * show which units already overlap the same period — informational only,
   * never a capacity rejection (DEC-014/OQ-009).
   */
  private fetchSharedOccupancy(buildingId: string, amenityId: string, requestVersion: number): void {
    this.occupancyState.set({ status: 'loading' });

    this.leisureService
      .getSharedOccupancy(
        buildingId,
        amenityId,
        this.fromDate().toISOString(),
        this.toDate().toISOString()
      )
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (occupancy: SharedOccupancy) => {
          if (requestVersion === this.contextVersion) {
            this.occupancyState.set({ status: 'success', unitLabels: occupancy.unitLabels });
          }
        },
        error: (error: ApiError) => {
          if (requestVersion === this.contextVersion) {
            this.occupancyState.set({ status: 'error', error });
          }
        }
      });
  }

  create(): void {
    // create() may only run from an explicitly safe state. In particular,
    // once a create attempt ends in `unknown` (a network failure where the
    // client cannot tell whether the server ever received/created the
    // reservation), a second POST is never allowed for this same flow —
    // Shared+Shared reservations are compatible and the endpoint has no
    // idempotency key, so a retry here could produce a second, distinct
    // hold rather than deduplicating the first one.
    const currentStatus = this.createState().status;

    if (currentStatus !== 'idle' && currentStatus !== 'error') {
      return;
    }

    const buildingId = this.residentContext.activeBuildingId();
    const amenity = this.amenitySignal();
    const useType = this.form.controls.useType.value;
    const quoteState = this.quoteState();

    if (!buildingId || !amenity || !useType || quoteState.status !== 'success') {
      return;
    }

    const fromDate = this.fromDate();
    const toDate = this.toDate();
    const validation = validateAvailabilityRange(fromDate, toDate);

    if (!validation.valid) {
      this.rangeError.set(validation.reason);
      return;
    }

    this.rangeError.set(null);
    this.createState.set({ status: 'creating' });

    const quotedAt = quoteState.quote;
    const requestVersion = this.contextVersion;

    this.leisureService
      .create({
        buildingId,
        amenityId: amenity.id,
        useType,
        startsAtUtc: fromDate.toISOString(),
        endsAtUtc: toDate.toISOString()
      })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (reservation) => {
          // The resident switched to a different amenity/use type while
          // this POST was in flight. We cannot know whether the backend
          // actually created this reservation — unsubscribing/ignoring is
          // a client-side view decision only, never a server-side
          // cancellation, and we never fire a compensating request. We
          // only make sure this late 201 is never shown as a confirmation
          // under a context the resident has already moved away from; #50
          // is the future way to find a reservation created this way.
          if (requestVersion === this.contextVersion) {
            this.createState.set({ status: 'created', reservation, quotedAt });
          }
        },
        error: (error: ApiError) => {
          if (requestVersion !== this.contextVersion) {
            return;
          }

          // status 0: the request never reached the server, or it did and
          // the response was lost — the client cannot tell which. Never
          // claim the reservation was not created.
          this.createState.set(error.status === 0 ? { status: 'unknown' } : { status: 'error', error });
        }
      });
  }

  resetFlow(): void {
    const options = this.eligibleUseTypes();
    const range = defaultAvailabilityRange();

    this.createState.set({ status: 'idle' });
    this.quoteState.set({ status: 'idle' });
    this.rangeError.set(null);
    this.useTypeError.set(null);
    this.form.reset({
      useType: options.length === 1 ? options[0] : null,
      from: toDateTimeLocalInputValue(range.fromUtc),
      to: toDateTimeLocalInputValue(range.toUtc)
    });
  }

  private fromDate(): Date {
    return new Date(this.form.controls.from.value);
  }

  private toDate(): Date {
    return new Date(this.form.controls.to.value);
  }
}
