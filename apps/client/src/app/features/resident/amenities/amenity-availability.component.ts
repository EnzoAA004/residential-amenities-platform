import { Component, Input, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { IonButton, IonNote, IonSpinner, IonText } from '@ionic/angular';
import { Subject, catchError, map, of, startWith, switchMap } from 'rxjs';

import { ApiError } from '../../../core/api/api-error';
import { AmenitiesService } from './amenities.service';
import { AmenitySummary, AvailabilityInterval } from './amenities.models';
import { GroupedAvailabilityDay, groupIntervalsByLocalDay } from './availability-grouping';
import {
  defaultAvailabilityRange,
  toDateTimeLocalInputValue,
  validateAvailabilityRange
} from './availability-range';

type AvailabilityLoadState =
  | { status: 'idle' }
  | { status: 'loading' }
  | { status: 'success'; intervals: AvailabilityInterval[] }
  | { status: 'empty' }
  | { status: 'error'; error: ApiError };

interface AvailabilityRequest {
  amenityId: string;
  fromUtc: string;
  toUtc: string;
}

/**
 * Lets the resident browse an amenity's structural availability over a
 * bounded range. It never creates a reservation and never computes
 * availability itself — it only sends `fromUtc`/`toUtc` and renders exactly
 * what `AmenitiesService.getAvailability` returns.
 *
 * A change of `amenity` (the parent swaps the selected amenity) resets any
 * in-flight/previous result to `idle` instead of leaving a stale amenity's
 * availability on screen; switching amenities does not by itself trigger a
 * new request — the resident presses "View availability" again, which also
 * keeps this from firing repeated requests while browsing.
 */
@Component({
  selector: 'app-amenity-availability',
  standalone: true,
  imports: [ReactiveFormsModule, IonButton, IonNote, IonSpinner, IonText],
  styles: [
    `
      .availability-form {
        display: grid;
        gap: var(--app-space-3);
        margin-block-end: var(--app-space-3);
      }

      .availability-form__fields {
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

      .availability-disclaimer {
        display: block;
        margin-block-end: var(--app-space-3);
      }

      .availability-day {
        border: 1px solid var(--app-border-color);
        border-radius: var(--app-radius-md);
        padding: var(--app-space-3);
        margin-block-end: var(--app-space-3);
      }

      .availability-day h3 {
        margin-block-start: 0;
      }

      .availability-day ul {
        margin: 0;
        padding-inline-start: var(--app-space-4);
      }
    `
  ],
  template: `
    <form class="availability-form" [formGroup]="form" (ngSubmit)="search()">
      <div class="availability-form__fields">
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

      <ion-button type="submit" [disabled]="state().status === 'loading'">
        {{ state().status === 'loading' ? 'Cargando…' : 'Ver disponibilidad' }}
      </ion-button>
    </form>

    <ion-text color="medium" class="availability-disclaimer">
      Esto muestra el horario base del espacio según su configuración.
      La disponibilidad final se valida al confirmar una reserva.
    </ion-text>

    @switch (state().status) {
      @case ('idle') {
        <p>
          <ion-text color="medium">Elegí un rango y consultá su disponibilidad.</ion-text>
        </p>
      }
      @case ('loading') {
        <p aria-live="polite">
          <ion-spinner name="dots" /> <ion-text color="medium">Cargando disponibilidad…</ion-text>
        </p>
      }
      @case ('empty') {
        <p aria-live="polite">
          <ion-text color="medium">No hay franjas habilitadas para este rango.</ion-text>
        </p>
      }
      @case ('error') {
        <p aria-live="assertive">
          <ion-text color="danger">{{ errorTitle() }}</ion-text>
        </p>
        <ion-button type="button" fill="outline" (click)="search()">Reintentar</ion-button>
      }
      @case ('success') {
        <div aria-live="polite">
          @for (day of groupedDays(); track day.dateLabel) {
            <section class="availability-day">
              <h3>{{ day.dateLabel }}</h3>
              <ul>
                @for (interval of day.intervals; track interval.startLabel + interval.endLabel) {
                  <li>{{ interval.startLabel }}–{{ interval.endLabel }}</li>
                }
              </ul>
            </section>
          }
        </div>
      }
    }
  `
})
export class AmenityAvailabilityComponent {
  private readonly amenitiesService = inject(AmenitiesService);
  private readonly requestSubject = new Subject<AvailabilityRequest | null>();
  private static nextInstanceId = 0;
  private readonly instanceId = AmenityAvailabilityComponent.nextInstanceId++;

  readonly fromInputId = `amenity-availability-from-${this.instanceId}`;
  readonly toInputId = `amenity-availability-to-${this.instanceId}`;

  private currentAmenityId: string | null = null;

  readonly form = new FormGroup({
    from: new FormControl(toDateTimeLocalInputValue(defaultAvailabilityRange().fromUtc), {
      nonNullable: true
    }),
    to: new FormControl(toDateTimeLocalInputValue(defaultAvailabilityRange().toUtc), {
      nonNullable: true
    })
  });

  readonly rangeError = signal<string | null>(null);

  readonly state = toSignal(
    this.requestSubject.pipe(
      switchMap((request) => {
        if (!request) {
          return of<AvailabilityLoadState>({ status: 'idle' });
        }

        return this.amenitiesService
          .getAvailability(request.amenityId, request.fromUtc, request.toUtc)
          .pipe(
            map((intervals): AvailabilityLoadState =>
              intervals.length > 0 ? { status: 'success', intervals } : { status: 'empty' }
            ),
            catchError((error: ApiError) => of<AvailabilityLoadState>({ status: 'error', error })),
            startWith<AvailabilityLoadState>({ status: 'loading' })
          );
      })
    ),
    { initialValue: { status: 'idle' } as AvailabilityLoadState }
  );

  readonly groupedDays = computed<GroupedAvailabilityDay[]>(() => {
    const currentState = this.state();
    return currentState.status === 'success' ? groupIntervalsByLocalDay(currentState.intervals) : [];
  });

  readonly errorTitle = computed(() => {
    const currentState = this.state();
    return currentState.status === 'error' ? currentState.error.title : '';
  });

  @Input({ required: true })
  set amenity(value: AmenitySummary) {
    if (this.currentAmenityId === value.id) {
      return;
    }

    this.currentAmenityId = value.id;
    this.rangeError.set(null);
    this.requestSubject.next(null);
  }

  search(): void {
    const fromUtc = new Date(this.form.controls.from.value);
    const toUtc = new Date(this.form.controls.to.value);
    const validation = validateAvailabilityRange(fromUtc, toUtc);

    if (!validation.valid) {
      this.rangeError.set(validation.reason);
      return;
    }

    if (!this.currentAmenityId) {
      return;
    }

    this.rangeError.set(null);
    this.requestSubject.next({
      amenityId: this.currentAmenityId,
      fromUtc: fromUtc.toISOString(),
      toUtc: toUtc.toISOString()
    });
  }
}
