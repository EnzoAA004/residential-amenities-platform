import { Component, computed, effect, inject, signal, untracked } from '@angular/core';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { IonBadge, IonButton, IonItem, IonLabel, IonSelect, IonSelectOption, IonSpinner, IonText } from '@ionic/angular';
import { Subject, catchError, map, merge, of, startWith, switchMap } from 'rxjs';

import { ApiError } from '../../../core/api/api-error';
import { ResidentContextStore } from '../../../core/resident-context/resident-context.store';
import { AppShellComponent } from '../../../layout/app-shell/app-shell.component';
import { AmenitiesService } from './amenities.service';
import { AmenitySummary } from './amenities.models';
import { AmenityAvailabilityComponent } from './amenity-availability.component';
import { LeisureReservationComponent } from '../reservations/leisure/leisure-reservation.component';
import { EventReservationComponent } from '../reservations/event/event-reservation.component';

type AmenitiesLoadState =
  | { status: 'idle' }
  | { status: 'loading' }
  | { status: 'success'; amenities: AmenitySummary[] }
  | { status: 'empty' }
  | { status: 'error'; error: ApiError };

/**
 * Resident building/amenity context and browsing (issue #46): building
 * selection, amenity listing, and an amenity's structural availability via
 * `AmenityAvailabilityComponent`. Once an amenity is selected, it also
 * hosts the Shared/Exclusive Leisure reservation flow
 * (`LeisureReservationComponent`, issue #47), and — only when the selected
 * amenity is a SUM (`kind === 'Sum'`) — the Event reservation flow
 * (`EventReservationComponent`, issue #48), passing it the same
 * already-loaded amenities list as add-on candidates. Both flows quote,
 * confirm and create a `Pending` hold. This page itself never computes
 * availability or a price client-side; see the client README for all
 * three issues' details.
 */
@Component({
  selector: 'app-amenities-page',
  standalone: true,
  imports: [
    AppShellComponent,
    AmenityAvailabilityComponent,
    LeisureReservationComponent,
    EventReservationComponent,
    IonBadge,
    IonButton,
    IonItem,
    IonLabel,
    IonSelect,
    IonSelectOption,
    IonSpinner,
    IonText
  ],
  styles: [
    `
      .building-selector {
        margin-block-end: var(--app-space-4);
        max-width: 360px;
      }

      .amenity-list {
        display: grid;
        gap: var(--app-space-3);
        margin-block-end: var(--app-space-4);
      }

      .amenity-card {
        border: 1px solid var(--app-border-color);
        border-radius: var(--app-radius-md);
        padding: var(--app-space-3);
        text-align: start;
        background: var(--app-surface);
        cursor: pointer;
        display: grid;
        gap: var(--app-space-1);
      }

      .amenity-card[aria-pressed='true'] {
        border-color: var(--ion-color-primary, #3880ff);
        box-shadow: var(--app-elevation-1);
      }

      .amenity-card__badges {
        display: flex;
        gap: var(--app-space-2);
        flex-wrap: wrap;
      }
    `
  ],
  template: `
    <app-shell title="Amenities">
      @if (residentContext.hasNoMembership()) {
        <p>
          <ion-text color="medium">No tenés una membresía residencial activa disponible.</ion-text>
        </p>
      } @else {
        @if (memberships().length > 1) {
          <div class="building-selector">
            <ion-item>
              <ion-label id="building-selector-label">Edificio</ion-label>
              <ion-select
                aria-labelledby="building-selector-label"
                interface="popover"
                placeholder="Elegí un edificio"
                [value]="residentContext.activeEdificioId()"
                (ionChange)="onEdificioChange($event)"
              >
                @for (membership of memberships(); track membership.buildingId) {
                  <ion-select-option [value]="membership.buildingId">
                    {{ membership.building }} — {{ membership.unit }}
                  </ion-select-option>
                }
              </ion-select>
            </ion-item>
          </div>
        }

        @if (residentContext.activeEdificioId(); as buildingId) {
          @switch (amenitiesState().status) {
            @case ('loading') {
              <p aria-live="polite">
                <ion-spinner name="dots" /> <ion-text color="medium">Cargando amenities…</ion-text>
              </p>
            }
            @case ('empty') {
              <p>
                <ion-text color="medium">Este edificio no tiene amenities activas.</ion-text>
              </p>
            }
            @case ('error') {
              <p role="alert">
                <ion-text color="danger">{{ amenitiesErrorTitle() }}</ion-text>
              </p>
              <ion-button type="button" fill="outline" (click)="retryAmenities()">Reintentar</ion-button>
            }
            @case ('success') {
              <ul class="amenity-list" role="list">
                @for (amenity of amenitiesList(); track amenity.id) {
                  <li>
                    <button
                      type="button"
                      class="amenity-card"
                      [attr.aria-pressed]="selectedAmenity()?.id === amenity.id"
                      (click)="selectAmenity(amenity)"
                    >
                      <strong>{{ amenity.name }}</strong>
                      <ion-text color="medium">{{ amenity.kind }}</ion-text>
                      <div class="amenity-card__badges">
                        @if (amenity.allowsSharedUse) {
                          <ion-badge color="medium">Uso compartido</ion-badge>
                        }
                        @if (amenity.allowsExclusiveUse) {
                          <ion-badge color="medium">Uso exclusivo</ion-badge>
                        }
                      </div>
                    </button>
                  </li>
                }
              </ul>
            }
          }

          @if (selectedAmenity(); as amenity) {
            <h2>{{ amenity.name }}</h2>
            <app-amenity-availability [amenity]="amenity" />
            <app-leisure-reservation [amenity]="amenity" />
            @if (amenity.kind === 'Sum') {
              <app-event-reservation [baseAmenity]="amenity" [amenities]="amenitiesList()" />
            }
          }
        }
      }
    </app-shell>
  `
})
export class AmenitiesPage {
  protected readonly residentContext = inject(ResidentContextStore);
  private readonly amenitiesService = inject(AmenitiesService);

  readonly memberships = this.residentContext.memberships;
  readonly selectedAmenity = signal<AmenitySummary | null>(null);

  private readonly retrySubject = new Subject<void>();

  protected readonly amenitiesState = toSignal(
    merge(
      toObservable(this.residentContext.activeEdificioId),
      this.retrySubject.pipe(map(() => this.residentContext.activeEdificioId()))
    ).pipe(
      switchMap((buildingId) => {
        if (!buildingId) {
          return of<AmenitiesLoadState>({ status: 'idle' });
        }

        return this.amenitiesService.listForEdificio(buildingId).pipe(
          map((amenities): AmenitiesLoadState =>
            amenities.length > 0 ? { status: 'success', amenities } : { status: 'empty' }
          ),
          catchError((error: ApiError) => of<AmenitiesLoadState>({ status: 'error', error })),
          startWith<AmenitiesLoadState>({ status: 'loading' })
        );
      })
    ),
    { initialValue: { status: 'idle' } as AmenitiesLoadState }
  );

  readonly amenitiesList = computed(() => {
    const state = this.amenitiesState();
    return state.status === 'success' ? state.amenities : [];
  });

  readonly amenitiesErrorTitle = computed(() => {
    const state = this.amenitiesState();
    return state.status === 'error' ? state.error.title : '';
  });

  constructor() {
    // A building change (including no building at all) always drops any
    // amenity selected under the previous building — never carries a stale
    // amenity/availability context across buildings.
    effect(() => {
      this.residentContext.activeEdificioId();
      untracked(() => this.selectedAmenity.set(null));
    });
  }

  onEdificioChange(event: CustomEvent<{ value: string }>): void {
    const buildingId = event.detail.value;
    if (buildingId) {
      this.residentContext.selectEdificio(buildingId);
    }
  }

  selectAmenity(amenity: AmenitySummary): void {
    this.selectedAmenity.set(amenity);
  }

  retryAmenities(): void {
    this.retrySubject.next();
  }
}
