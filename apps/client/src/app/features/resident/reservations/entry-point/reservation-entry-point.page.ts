import { Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { IonBadge, IonButton, IonNote, IonSpinner, IonText } from '@ionic/angular';
import { catchError, map, of, startWith, switchMap } from 'rxjs';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';

import { ApiError } from '../../../../core/api/api-error';
import { ResidentContextStore } from '../../../../core/resident-context/resident-context.store';
import { AppShellComponent } from '../../../../layout/app-shell/app-shell.component';
import { AppErrorStateComponent } from '../../../../shared/error-state/app-error-state.component';
import { AmenitiesService } from '../../amenities/amenities.service';
import { AmenitySummary } from '../../amenities/amenities.models';
import { AmenityAvailabilityComponent } from '../../amenities/amenity-availability.component';
import { EventReservationComponent } from '../event/event-reservation.component';
import { LeisureReservationComponent } from '../leisure/leisure-reservation.component';
import { LeisureUseType } from '../leisure/leisure-reservation.models';
import {
  ReservationEntryPoint,
  ReservationEntryPointSuggestedUseType
} from './reservation-entry-point.models';
import { ReservationEntryPointService } from './reservation-entry-point.service';

type EntryPointState =
  | { status: 'loading' }
  | { status: 'success'; entryPoint: ReservationEntryPoint; amenity: AmenitySummary }
  | { status: 'error'; error: ApiError }
  | { status: 'no-membership-context'; entryPoint: ReservationEntryPoint; amenity: AmenitySummary };

type AmenitiesListState =
  | { status: 'idle' }
  | { status: 'loading' }
  | { status: 'success'; amenities: AmenitySummary[] }
  | { status: 'error'; error: ApiError };

@Component({
  selector: 'app-reservation-entry-point-page',
  standalone: true,
  imports: [
    AppShellComponent,
    AppErrorStateComponent,
    AmenityAvailabilityComponent,
    EventReservationComponent,
    LeisureReservationComponent,
    RouterLink,
    IonBadge,
    IonButton,
    IonNote,
    IonSpinner,
    IonText
  ],
  styles: [
    `
      .entry-summary {
        display: grid;
        gap: var(--app-space-2);
        margin-block-end: var(--app-space-4);
      }

      .entry-summary__badges {
        display: flex;
        gap: var(--app-space-2);
        flex-wrap: wrap;
      }

      .flow-stack {
        display: grid;
        gap: var(--app-space-4);
      }
    `
  ],
  template: `
    <app-shell title="Reservar amenity">
      @switch (entryPointState().status) {
        @case ('loading') {
          <p aria-live="polite">
            <ion-spinner name="dots" /> <ion-text color="medium">Resolviendo QR…</ion-text>
          </p>
        }
        @case ('error') {
          <app-error-state
            [title]="entryPointErrorTitle()"
            [detail]="entryPointErrorDetail() ?? undefined"
          />
          <p>
            <ion-button routerLink="/amenities" fill="outline">Ver amenities</ion-button>
          </p>
        }
        @case ('no-membership-context') {
          <app-error-state
            title="No pudimos activar el edificio de este QR"
            detail="El backend autorizó el token, pero tu sesión local no tiene una membresía activa para ese edificio. Volvé a iniciar sesión y probá de nuevo."
          />
        }
        @case ('success') {
          @if (entryPointState(); as state) {
            @if (state.status === 'success') {
              <section class="entry-summary" aria-labelledby="entry-title">
                <h1 id="entry-title">{{ state.entryPoint.displayName }}</h1>
                <ion-text color="medium">{{ state.entryPoint.amenityName }} · {{ state.entryPoint.amenityKind }}</ion-text>
                <div class="entry-summary__badges">
                  @if (state.entryPoint.allowsSharedUse) {
                    <ion-badge color="medium">Shared use</ion-badge>
                  }
                  @if (state.entryPoint.allowsExclusiveUse) {
                    <ion-badge color="medium">Exclusive use</ion-badge>
                  }
                  @if (state.entryPoint.suggestedUseType) {
                    <ion-badge color="primary">{{ suggestedUseTypeLabel(state.entryPoint.suggestedUseType) }}</ion-badge>
                  }
                </div>
              </section>

              <app-amenity-availability [amenity]="state.amenity" />

              @if (isReservable(state.entryPoint)) {
                <div class="flow-stack">
                  @if (showEventFirst(state.entryPoint)) {
                    @if (state.amenity.kind === 'Sum' && state.amenity.allowsExclusiveUse) {
                      @switch (amenitiesListState().status) {
                        @case ('loading') {
                          <p aria-live="polite">
                            <ion-spinner name="dots" /> <ion-text color="medium">Cargando opciones de evento…</ion-text>
                          </p>
                        }
                        @case ('error') {
                          <app-error-state
                            [title]="amenitiesListErrorTitle()"
                            [detail]="amenitiesListErrorDetail() ?? undefined"
                          />
                        }
                        @case ('success') {
                          <app-event-reservation [baseAmenity]="state.amenity" [amenities]="amenitiesList()" />
                        }
                      }
                    }
                    @if (hasLeisureFlow(state.entryPoint)) {
                      <app-leisure-reservation
                        [amenity]="state.amenity"
                        [preferredUseType]="preferredLeisureUseType(state.entryPoint)"
                      />
                    }
                  } @else {
                    @if (hasLeisureFlow(state.entryPoint)) {
                      <app-leisure-reservation
                        [amenity]="state.amenity"
                        [preferredUseType]="preferredLeisureUseType(state.entryPoint)"
                      />
                    }
                    @if (state.amenity.kind === 'Sum' && state.amenity.allowsExclusiveUse) {
                      @switch (amenitiesListState().status) {
                        @case ('loading') {
                          <p aria-live="polite">
                            <ion-spinner name="dots" /> <ion-text color="medium">Cargando opciones de evento…</ion-text>
                          </p>
                        }
                        @case ('error') {
                          <app-error-state
                            [title]="amenitiesListErrorTitle()"
                            [detail]="amenitiesListErrorDetail() ?? undefined"
                          />
                        }
                        @case ('success') {
                          <app-event-reservation [baseAmenity]="state.amenity" [amenities]="amenitiesList()" />
                        }
                      }
                    }
                  }
                </div>
              } @else {
                <p>
                  <ion-note color="warning">Este recurso no tiene un flujo de reserva disponible.</ion-note>
                </p>
              }
            }
          }
        }
      }
    </app-shell>
  `
})
export class ReservationEntryPointPage {
  private readonly route = inject(ActivatedRoute);
  private readonly entryPointService = inject(ReservationEntryPointService);
  private readonly amenitiesService = inject(AmenitiesService);
  private readonly residentContext = inject(ResidentContextStore);

  private readonly buildingIdForAmenities = signal<string | null>(null);

  readonly entryPointState = toSignal(
    this.route.paramMap.pipe(
      map((params) => params.get('token')?.trim() ?? ''),
      switchMap((token) => {
        this.buildingIdForAmenities.set(null);

        if (!token) {
          return of<EntryPointState>({
            status: 'error',
            error: { status: 404, title: 'QR inválido.' }
          });
        }

        return this.entryPointService.resolve(token).pipe(
          map((entryPoint): EntryPointState => {
            const amenity = this.toAmenitySummary(entryPoint);
            const selected = this.residentContext.selectBuilding(entryPoint.buildingId);

            if (!selected && this.residentContext.activeBuildingId() !== entryPoint.buildingId) {
              return { status: 'no-membership-context', entryPoint, amenity };
            }

            this.buildingIdForAmenities.set(this.supportsEventFlow(entryPoint) ? entryPoint.buildingId : null);
            return { status: 'success', entryPoint, amenity };
          }),
          catchError((error: ApiError) => of<EntryPointState>({ status: 'error', error })),
          startWith<EntryPointState>({ status: 'loading' })
        );
      })
    ),
    { initialValue: { status: 'loading' } as EntryPointState }
  );

  readonly amenitiesListState = toSignal(
    toObservable(this.buildingIdForAmenities).pipe(
      switchMap((buildingId) => {
        if (!buildingId) {
          return of<AmenitiesListState>({ status: 'idle' });
        }

        return this.amenitiesService.listForBuilding(buildingId).pipe(
          map((amenities): AmenitiesListState => ({ status: 'success', amenities })),
          catchError((error: ApiError) => of<AmenitiesListState>({ status: 'error', error })),
          startWith<AmenitiesListState>({ status: 'loading' })
        );
      })
    ),
    { initialValue: { status: 'idle' } as AmenitiesListState }
  );

  readonly amenitiesList = computed(() => {
    const state = this.amenitiesListState();
    return state.status === 'success' ? state.amenities : [];
  });

  entryPointErrorTitle(): string {
    const state = this.entryPointState();
    return state.status === 'error' ? this.mapEntryPointErrorTitle(state.error) : '';
  }

  entryPointErrorDetail(): string | null {
    const state = this.entryPointState();
    return state.status === 'error' ? (state.error.detail ?? null) : null;
  }

  amenitiesListErrorTitle(): string {
    const state = this.amenitiesListState();
    return state.status === 'error' ? state.error.title : '';
  }

  amenitiesListErrorDetail(): string | null {
    const state = this.amenitiesListState();
    return state.status === 'error' ? (state.error.detail ?? null) : null;
  }

  hasLeisureFlow(entryPoint: ReservationEntryPoint): boolean {
    return entryPoint.allowsSharedUse || entryPoint.allowsExclusiveUse;
  }

  isReservable(entryPoint: ReservationEntryPoint): boolean {
    return this.hasLeisureFlow(entryPoint) || this.supportsEventFlow(entryPoint);
  }

  showEventFirst(entryPoint: ReservationEntryPoint): boolean {
    return entryPoint.suggestedUseType === 'Event';
  }

  preferredLeisureUseType(entryPoint: ReservationEntryPoint): LeisureUseType | null {
    return entryPoint.suggestedUseType === 'SharedLeisure' ||
      entryPoint.suggestedUseType === 'ExclusiveLeisure'
      ? entryPoint.suggestedUseType
      : null;
  }

  suggestedUseTypeLabel(useType: ReservationEntryPointSuggestedUseType): string {
    switch (useType) {
      case 'SharedLeisure':
        return 'Shared suggested';
      case 'ExclusiveLeisure':
        return 'Exclusive suggested';
      case 'Event':
        return 'Event suggested';
    }
  }

  private supportsEventFlow(entryPoint: ReservationEntryPoint): boolean {
    return entryPoint.amenityKind === 'Sum' && entryPoint.allowsExclusiveUse;
  }

  private toAmenitySummary(entryPoint: ReservationEntryPoint): AmenitySummary {
    return {
      id: entryPoint.amenityId,
      name: entryPoint.amenityName,
      kind: entryPoint.amenityKind,
      allowsSharedUse: entryPoint.allowsSharedUse,
      allowsExclusiveUse: entryPoint.allowsExclusiveUse
    };
  }

  private mapEntryPointErrorTitle(error: ApiError): string {
    if (error.status === 404) {
      return 'QR inválido o inactivo.';
    }

    if (error.status === 403) {
      return 'No tenés acceso a este edificio.';
    }

    return error.title;
  }
}
