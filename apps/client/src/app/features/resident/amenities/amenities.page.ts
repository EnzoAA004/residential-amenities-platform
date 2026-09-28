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

type AmenitiesLoadState =
  | { status: 'idle' }
  | { status: 'loading' }
  | { status: 'success'; amenities: AmenitySummary[] }
  | { status: 'empty' }
  | { status: 'error'; error: ApiError };

/**
 * Resident-facing amenity and structural-availability browsing (issue #46).
 * This page never creates a reservation and never computes availability
 * itself — see `AmenityAvailabilityComponent` and the client README.
 */
@Component({
  selector: 'app-amenities-page',
  standalone: true,
  imports: [
    AppShellComponent,
    AmenityAvailabilityComponent,
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
              <ion-label id="building-selector-label">Building</ion-label>
              <ion-select
                aria-labelledby="building-selector-label"
                interface="popover"
                placeholder="Choose a building"
                [value]="residentContext.activeBuildingId()"
                (ionChange)="onBuildingChange($event)"
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

        @if (residentContext.activeBuildingId(); as buildingId) {
          @switch (amenitiesState().status) {
            @case ('loading') {
              <p aria-live="polite">
                <ion-spinner name="dots" /> <ion-text color="medium">Loading amenities…</ion-text>
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
              <ion-button type="button" fill="outline" (click)="retryAmenities()">Retry</ion-button>
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
                          <ion-badge color="medium">Shared use</ion-badge>
                        }
                        @if (amenity.allowsExclusiveUse) {
                          <ion-badge color="medium">Exclusive use</ion-badge>
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
      toObservable(this.residentContext.activeBuildingId),
      this.retrySubject.pipe(map(() => this.residentContext.activeBuildingId()))
    ).pipe(
      switchMap((buildingId) => {
        if (!buildingId) {
          return of<AmenitiesLoadState>({ status: 'idle' });
        }

        return this.amenitiesService.listForBuilding(buildingId).pipe(
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
      this.residentContext.activeBuildingId();
      untracked(() => this.selectedAmenity.set(null));
    });
  }

  onBuildingChange(event: CustomEvent<{ value: string }>): void {
    const buildingId = event.detail.value;
    if (buildingId) {
      this.residentContext.selectBuilding(buildingId);
    }
  }

  selectAmenity(amenity: AmenitySummary): void {
    this.selectedAmenity.set(amenity);
  }

  retryAmenities(): void {
    this.retrySubject.next();
  }
}
