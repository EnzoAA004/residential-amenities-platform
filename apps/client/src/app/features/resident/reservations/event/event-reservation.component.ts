import { Component, DestroyRef, Input, computed, effect, inject, signal, untracked } from '@angular/core';
import { takeUntilDestroyed, toObservable, toSignal } from '@angular/core/rxjs-interop';
import { IonButton, IonNote, IonSpinner, IonText } from '@ionic/angular';
import { Subject, catchError, map, merge, of, startWith, switchMap } from 'rxjs';

import { ApiError } from '../../../../core/api/api-error';
import { ResidentContextStore } from '../../../../core/resident-context/resident-context.store';
import { AmenitySummary } from '../../amenities/amenities.models';
import { PriceQuote, Reservation } from '../reservation.models';
import { EventReservationService } from './event-reservation.service';
import { EventSlotOccurrence } from './event-reservation.models';

type SlotsLoadState =
  | { status: 'idle' }
  | { status: 'loading' }
  | { status: 'success'; slots: EventSlotOccurrence[] }
  | { status: 'empty' }
  | { status: 'error'; error: ApiError };

type QuoteState =
  | { status: 'idle' }
  | { status: 'loading' }
  | { status: 'success'; quote: PriceQuote }
  | { status: 'error'; error: ApiError };

type CreateState =
  | { status: 'idle' }
  | { status: 'creating' }
  | { status: 'created'; reservation: Reservation; quotedAt: PriceQuote | null }
  | { status: 'error'; error: ApiError }
  | { status: 'unknown' };

/**
 * The Event reservation flow (issue #48): pick a building-local calendar
 * date, choose one of the backend's configured & active Event slot
 * occurrences for that date (issue #62), optionally add Pool/Barbecue
 * add-ons, quote, confirm, create a `Pending` hold. Ends there — payment
 * (#49) and full-day slots (out of MVP, issue #2) are out of scope.
 *
 * `buildingId` always comes from `ResidentContextStore.activeMembership()`;
 * `baseAmenity` and the add-on candidates always come from the building's
 * already-loaded amenities list (`AmenitiesPage.amenitiesList()`) — never
 * an editable id. The selected slot's `startsAtUtc`/`endsAtUtc` are sent to
 * `POST /api/reservations` byte-for-byte as the backend returned them;
 * this component never reconstructs them from the chosen date using the
 * device's time zone.
 */
@Component({
  selector: 'app-event-reservation',
  standalone: true,
  imports: [IonButton, IonNote, IonSpinner, IonText],
  styles: [
    `
      .event-form {
        display: grid;
        gap: var(--app-space-3);
        margin-block: var(--app-space-3);
      }

      label {
        display: grid;
        gap: var(--app-space-1);
        font-size: var(--app-font-size-sm);
        max-width: 220px;
      }

      input[type='date'] {
        border: 1px solid var(--app-border-color);
        border-radius: var(--app-radius-sm);
        padding: var(--app-space-2);
        font: inherit;
      }

      .slot-options,
      .addon-options {
        display: flex;
        gap: var(--app-space-2);
        flex-wrap: wrap;
      }

      .slot-option {
        border: 1px solid var(--app-border-color);
        border-radius: var(--app-radius-sm);
        padding: var(--app-space-2) var(--app-space-3);
        background: var(--app-surface);
        cursor: pointer;
      }

      .slot-option[aria-pressed='true'] {
        border-color: var(--ion-color-primary, #3880ff);
        box-shadow: var(--app-elevation-1);
      }

      .addon-option {
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
    @if (!baseAllowsExclusiveUse()) {
      <p>
        <ion-text color="medium">Este SUM no admite reservas de tipo Event.</ion-text>
      </p>
    } @else if (createState(); as createStatus) {
      @if (createStatus.status === 'created') {
        <section class="confirmation-card" aria-live="polite">
          <h3>Event reservation hold created</h3>
          <dl>
            <dt>Amenity</dt>
            <dd>{{ baseAmenityName() }}</dd>
            <dt>Date</dt>
            <dd>{{ formattedSelectedDate() }}</dd>
            <dt>Slot</dt>
            <dd>{{ confirmedSlotName() }}</dd>
            @if (confirmedAddOnNames().length > 0) {
              <dt>Add-ons</dt>
              <dd>{{ confirmedAddOnNames().join(', ') }}</dd>
            }
            <dt>Status</dt>
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

          <p>
            <ion-text color="medium">El pago se realizará en el siguiente paso.</ion-text>
          </p>

          <ion-button type="button" fill="outline" (click)="resetFlow()">
            Create another reservation
          </ion-button>
        </section>
      } @else {
        <form class="event-form" (ngSubmit)="requestQuote()">
          <label [attr.for]="dateInputId">
            Date
            <input
              [id]="dateInputId"
              type="date"
              [value]="selectedDate()"
              (change)="onDateChange($event)"
            />
          </label>

          @if (selectedDate()) {
            @switch (slotsState().status) {
              @case ('loading') {
                <p aria-live="polite">
                  <ion-spinner name="dots" /> <ion-text color="medium">Loading slots…</ion-text>
                </p>
              }
              @case ('empty') {
                <p>
                  <ion-text color="medium">No hay franjas Event configuradas para esta fecha.</ion-text>
                </p>
              }
              @case ('error') {
                <p role="alert">
                  <ion-text color="danger">{{ slotsErrorTitle() }}</ion-text>
                  @if (slotsErrorDetail(); as detail) {
                    <br />
                    <ion-text color="danger">{{ detail }}</ion-text>
                  }
                </p>
                <ion-button type="button" fill="clear" (click)="reloadSlots()">Retry</ion-button>
              }
              @case ('success') {
                <fieldset>
                  <legend>Slot</legend>
                  <div class="slot-options" role="list">
                    @for (slot of slotsList(); track slot.id) {
                      <button
                        type="button"
                        class="slot-option"
                        [attr.aria-pressed]="selectedSlot()?.id === slot.id"
                        (click)="selectSlot(slot)"
                      >
                        {{ slot.name }}
                      </button>
                    }
                  </div>
                </fieldset>
              }
            }
          }

          @if (eligibleAddOns().length > 0) {
            <fieldset>
              <legend>Add-ons (optional)</legend>
              <div class="addon-options">
                @for (addOn of eligibleAddOns(); track addOn.id) {
                  <label class="addon-option">
                    <input
                      type="checkbox"
                      [checked]="isAddOnSelected(addOn.id)"
                      (change)="toggleAddOn(addOn.id)"
                    />
                    {{ addOn.name }} ({{ addOn.kind }})
                  </label>
                }
              </div>
            </fieldset>
          }

          @if (selectedSlot()) {
            <ion-button type="submit" fill="outline" [disabled]="quoteState().status === 'loading'">
              {{ quoteState().status === 'loading' ? 'Cotizando…' : 'Get quote' }}
            </ion-button>
          }
        </form>

        @switch (quoteState().status) {
          @case ('loading') {
            <p aria-live="polite">
              <ion-spinner name="dots" /> <ion-text color="medium">Cotizando…</ion-text>
            </p>
          }
          @case ('error') {
            <p role="alert">
              <ion-text color="danger">{{ quoteErrorTitle() }}</ion-text>
              @if (quoteErrorDetail(); as detail) {
                <br />
                <ion-text color="danger">{{ detail }}</ion-text>
              }
            </p>
            <ion-button type="button" fill="clear" (click)="requestQuote()">Retry</ion-button>
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

        @if (quoteState().status === 'success') {
          <ion-button
            type="button"
            (click)="create()"
            [disabled]="createStatus.status === 'creating' || createStatus.status === 'unknown'"
          >
            {{ createStatus.status === 'creating' ? 'Creating…' : 'Confirm reservation' }}
          </ion-button>
        }

        @if (createStatus.status === 'error') {
          <p role="alert">
            <ion-text color="danger">{{ createErrorTitle() }}</ion-text>
            @if (createErrorDetail(); as detail) {
              <br />
              <ion-text color="danger">{{ detail }}</ion-text>
            }
          </p>
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
  `
})
export class EventReservationComponent {
  private readonly eventService = inject(EventReservationService);
  private readonly residentContext = inject(ResidentContextStore);
  private readonly destroyRef = inject(DestroyRef);
  private static nextInstanceId = 0;
  private readonly instanceId = EventReservationComponent.nextInstanceId++;

  readonly dateInputId = `event-reservation-date-${this.instanceId}`;

  private readonly baseAmenitySignal = signal<AmenitySummary | null>(null);
  private readonly amenitiesSignal = signal<AmenitySummary[]>([]);
  private currentBaseAmenityId: string | null = null;

  /**
   * Bumped on every change to the request context (base amenity, date,
   * slot or add-on selection). See `LeisureReservationComponent` (#47) for
   * the full rationale: this component is reused, not destroyed, when the
   * resident switches between two SUM amenities in the same building, so a
   * quote/create request already in flight for the previous context must
   * never be allowed to resolve into the new one.
   */
  private contextVersion = 0;

  readonly selectedDate = signal('');
  readonly selectedSlot = signal<EventSlotOccurrence | null>(null);
  private readonly selectedAddOnIdsSignal = signal<ReadonlySet<string>>(new Set());

  readonly quoteState = signal<QuoteState>({ status: 'idle' });
  readonly createState = signal<CreateState>({ status: 'idle' });

  readonly baseAmenityName = computed(() => this.baseAmenitySignal()?.name ?? '');

  /**
   * Event reservations require the base SUM to allow exclusive use
   * (`ReservationCreationService` rejects the base otherwise with a 422:
   * "The base amenity does not allow exclusive use... (RB-006)"). The
   * client mirrors that eligibility check so the resident never walks
   * through date/slot/quote only to hit that error at create time.
   */
  readonly baseAllowsExclusiveUse = computed(() => this.baseAmenitySignal()?.allowsExclusiveUse ?? false);

  readonly eligibleAddOns = computed<AmenitySummary[]>(() => {
    const base = this.baseAmenitySignal();

    if (!base) {
      return [];
    }

    return this.amenitiesSignal().filter(
      (candidate) =>
        candidate.id !== base.id &&
        (candidate.kind === 'Pool' || candidate.kind === 'Barbecue') &&
        candidate.allowsExclusiveUse
    );
  });

  private readonly slotsRetrySubject = new Subject<void>();

  readonly slotsState = toSignal(
    merge(
      toObservable(this.selectedDate),
      this.slotsRetrySubject.pipe(map(() => this.selectedDate()))
    ).pipe(
      switchMap((date) => {
        const buildingId = this.residentContext.activeBuildingId();

        if (!date || !buildingId) {
          return of<SlotsLoadState>({ status: 'idle' });
        }

        return this.eventService.listSlots(buildingId, date).pipe(
          map((slots): SlotsLoadState =>
            slots.length > 0 ? { status: 'success', slots } : { status: 'empty' }
          ),
          catchError((error: ApiError) => of<SlotsLoadState>({ status: 'error', error })),
          startWith<SlotsLoadState>({ status: 'loading' })
        );
      })
    ),
    { initialValue: { status: 'idle' } as SlotsLoadState }
  );

  readonly slotsList = computed(() => {
    const state = this.slotsState();
    return state.status === 'success' ? state.slots : [];
  });

  readonly slotsErrorTitle = computed(() => {
    const state = this.slotsState();
    return state.status === 'error' ? state.error.title : '';
  });

  readonly slotsErrorDetail = computed(() => {
    const state = this.slotsState();
    return state.status === 'error' ? (state.error.detail ?? null) : null;
  });

  private readonly dateFormatter = new Intl.DateTimeFormat(undefined, {
    dateStyle: 'long',
    timeZone: 'UTC'
  });

  private readonly dateTimeFormatter = new Intl.DateTimeFormat(undefined, {
    dateStyle: 'medium',
    timeStyle: 'short'
  });

  readonly formattedSelectedDate = computed(() => {
    const date = this.selectedDate();
    return date ? this.dateFormatter.format(new Date(`${date}T00:00:00Z`)) : '';
  });

  constructor() {
    // A date change always drops the previously selected slot — a slot
    // occurrence is only meaningful for the date it was fetched for.
    effect(() => {
      this.selectedDate();

      untracked(() => {
        this.selectedSlot.set(null);
        this.invalidateQuote();
      });
    });

    // If the amenities list changes (e.g. a refetch) and an add-on the
    // resident had selected is no longer eligible, drop it rather than
    // silently keep sending a stale id.
    effect(() => {
      const eligibleIds = new Set(this.eligibleAddOns().map((addOn) => addOn.id));

      untracked(() => {
        const current = this.selectedAddOnIdsSignal();
        const pruned = new Set([...current].filter((id) => eligibleIds.has(id)));

        if (pruned.size !== current.size) {
          this.selectedAddOnIdsSignal.set(pruned);
          this.invalidateQuote();
        }
      });
    });
  }

  @Input({ required: true })
  set baseAmenity(value: AmenitySummary) {
    if (this.currentBaseAmenityId === value.id) {
      return;
    }

    this.currentBaseAmenityId = value.id;
    this.baseAmenitySignal.set(value);
    this.selectedDate.set('');
    this.selectedSlot.set(null);
    this.selectedAddOnIdsSignal.set(new Set());
    this.createState.set({ status: 'idle' });
    this.invalidateQuote();
  }

  @Input({ required: true })
  set amenities(value: AmenitySummary[]) {
    this.amenitiesSignal.set(value);
  }

  onDateChange(event: Event): void {
    this.selectedDate.set((event.target as HTMLInputElement).value);
  }

  reloadSlots(): void {
    this.slotsRetrySubject.next();
  }

  selectSlot(slot: EventSlotOccurrence): void {
    this.selectedSlot.set(slot);
    this.invalidateQuote();
  }

  isAddOnSelected(amenityId: string): boolean {
    return this.selectedAddOnIdsSignal().has(amenityId);
  }

  toggleAddOn(amenityId: string): void {
    const current = new Set(this.selectedAddOnIdsSignal());

    if (current.has(amenityId)) {
      current.delete(amenityId);
    } else {
      current.add(amenityId);
    }

    this.selectedAddOnIdsSignal.set(current);
    this.invalidateQuote();
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

  confirmedSlotName(): string {
    return this.selectedSlot()?.name ?? '';
  }

  confirmedAddOnNames(): string[] {
    const selectedIds = this.selectedAddOnIdsSignal();
    return this.amenitiesSignal()
      .filter((amenity) => selectedIds.has(amenity.id))
      .map((amenity) => amenity.name);
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
    const base = this.baseAmenitySignal();
    const slot = this.selectedSlot();

    if (!buildingId || !base || !slot) {
      return;
    }

    this.quoteState.set({ status: 'loading' });

    const requestVersion = this.contextVersion;
    const addOnIds = Array.from(this.selectedAddOnIdsSignal());

    this.eventService
      .getQuote(buildingId, base.id, addOnIds)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (quote) => {
          if (requestVersion === this.contextVersion) {
            this.quoteState.set({ status: 'success', quote });
          }
        },
        error: (error: ApiError) => {
          if (requestVersion === this.contextVersion) {
            this.quoteState.set({ status: 'error', error });
          }
        }
      });
  }

  create(): void {
    // See LeisureReservationComponent (#47): create() only runs from an
    // explicitly safe state. A second POST after `unknown` (uncertain
    // network result) is never allowed for this same context.
    const currentStatus = this.createState().status;

    if (currentStatus !== 'idle' && currentStatus !== 'error') {
      return;
    }

    const buildingId = this.residentContext.activeBuildingId();
    const base = this.baseAmenitySignal();
    const slot = this.selectedSlot();
    const quoteState = this.quoteState();

    if (!buildingId || !base || !slot || quoteState.status !== 'success') {
      return;
    }

    this.createState.set({ status: 'creating' });

    const requestVersion = this.contextVersion;
    const addOnIds = Array.from(this.selectedAddOnIdsSignal());
    const quotedAt = quoteState.quote;

    this.eventService
      .create({
        buildingId,
        amenityId: base.id,
        addOnAmenityIds: addOnIds,
        useType: 'Event',
        // Copied verbatim from the selected occurrence — never
        // reconstructed from `selectedDate()` with a client-side timezone.
        startsAtUtc: slot.startsAtUtc,
        endsAtUtc: slot.endsAtUtc
      })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (reservation) => {
          if (requestVersion === this.contextVersion) {
            this.createState.set({ status: 'created', reservation, quotedAt });
          }
        },
        error: (error: ApiError) => {
          if (requestVersion !== this.contextVersion) {
            return;
          }

          this.createState.set(error.status === 0 ? { status: 'unknown' } : { status: 'error', error });
        }
      });
  }

  resetFlow(): void {
    this.createState.set({ status: 'idle' });
    this.selectedDate.set('');
    this.selectedSlot.set(null);
    this.selectedAddOnIdsSignal.set(new Set());
    this.invalidateQuote();
  }

  private invalidateQuote(): void {
    this.contextVersion++;
    this.quoteState.set({ status: 'idle' });
  }
}
