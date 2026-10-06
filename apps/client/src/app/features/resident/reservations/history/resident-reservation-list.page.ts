import { NgTemplateOutlet } from '@angular/common';
import { Component, computed, effect, inject, signal, untracked } from '@angular/core';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { IonButton, IonItem, IonLabel, IonSelect, IonSelectOption, IonSpinner, IonText } from '@ionic/angular';
import { Subject, catchError, distinctUntilChanged, map, merge, of, startWith, switchMap } from 'rxjs';

import { ApiError } from '../../../../core/api/api-error';
import { ResidentContextStore } from '../../../../core/resident-context/resident-context.store';
import { AppShellComponent } from '../../../../layout/app-shell/app-shell.component';
import { ResidentReservationPage } from './resident-reservation.models';
import { ResidentReservationService } from './resident-reservation.service';

type ReservationListState =
  | { status: 'idle' }
  | { status: 'loading' }
  | { status: 'success'; page: ResidentReservationPage }
  | { status: 'empty'; page: ResidentReservationPage }
  | { status: 'error'; error: ApiError };

@Component({
  selector: 'app-resident-reservation-list-page',
  standalone: true,
  imports: [
    AppShellComponent,
    NgTemplateOutlet,
    RouterLink,
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

      .reservation-list {
        display: grid;
        gap: var(--app-space-3);
        padding: 0;
        list-style: none;
      }

      .reservation-card {
        border: 1px solid var(--app-border-color);
        border-radius: var(--app-radius-md);
        padding: var(--app-space-3);
        display: grid;
        gap: var(--app-space-2);
      }

      .reservation-card dl {
        display: grid;
        grid-template-columns: auto 1fr;
        gap: var(--app-space-1) var(--app-space-3);
        margin: 0;
      }

      .reservation-card dt {
        font-weight: 600;
      }

      .reservation-card dd {
        margin: 0;
      }

      .pagination {
        display: flex;
        gap: var(--app-space-3);
        align-items: center;
        flex-wrap: wrap;
        margin-block-start: var(--app-space-4);
      }
    `
  ],
  template: `
    <app-shell title="Mis reservas">
      @if (residentContext.hasNoMembership()) {
        <p>
          <ion-text color="medium">No tenés una membresía residencial activa disponible.</ion-text>
        </p>
      } @else {
        @if (memberships().length > 1) {
          <div class="building-selector">
            <ion-item>
              <ion-label id="reservation-building-selector-label">Edificio</ion-label>
              <ion-select
                aria-labelledby="reservation-building-selector-label"
                interface="popover"
                placeholder="Elegí un edificio"
                [value]="residentContext.activeMembership()?.buildingId"
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

        @if (residentContext.activeMembership()?.buildingId) {
          @switch (reservationsState().status) {
            @case ('loading') {
              <p aria-live="polite">
                <ion-spinner name="dots" />
                <ion-text color="medium">Cargando reservas…</ion-text>
              </p>
            }
            @case ('empty') {
              <p>
                <ion-text color="medium">Todavía no tenés reservas para este edificio.</ion-text>
              </p>
              <ng-container *ngTemplateOutlet="pager" />
            }
            @case ('error') {
              <p role="alert">
                <ion-text color="danger">{{ reservationsErrorTitle() }}</ion-text>
                @if (reservationsErrorDetail(); as detail) {
                  <br />
                  <ion-text color="danger">{{ detail }}</ion-text>
                }
              </p>
              <ion-button type="button" fill="outline" (click)="retryReservations()">Reintentar</ion-button>
            }
            @case ('success') {
              <ul class="reservation-list" role="list">
                @for (reservation of reservationsPage()!.items; track reservation.id) {
                  <li class="reservation-card">
                    <dl>
                      <dt>Tipo</dt>
                      <dd>{{ reservation.useType }}</dd>
                      <dt>Estado</dt>
                      <dd>{{ reservation.status }}</dd>
                      <dt>Inicio</dt>
                      <dd>{{ formatDateTime(reservation.startsAtUtc) }}</dd>
                      <dt>Total</dt>
                      <dd>{{ formatAmount(reservation.totalAmount, reservation.currency) }}</dd>
                    </dl>
                    <ion-button type="button" fill="clear" [routerLink]="['/reservations', reservation.id]">
                      Ver detalle
                    </ion-button>
                  </li>
                }
              </ul>
              <ng-container *ngTemplateOutlet="pager" />
            }
          }
        } @else if (residentContext.requiresSelection()) {
          <p>
            <ion-text color="medium">Elegí un edificio para ver tus reservas.</ion-text>
          </p>
        }
      }

      <ng-template #pager>
        @if (reservationsPage(); as page) {
          <div class="pagination">
            <ion-button type="button" fill="outline" [disabled]="page.page <= 1" (click)="previousPage()">
              Anterior
            </ion-button>
            <span>Página {{ page.page }} · {{ page.totalCount }} reservas</span>
            <ion-button type="button" fill="outline" [disabled]="page.page * page.pageSize >= page.totalCount" (click)="nextPage()">
              Siguiente
            </ion-button>
          </div>
        }
      </ng-template>
    </app-shell>
  `
})
export class ResidentReservationListPage {
  protected readonly residentContext = inject(ResidentContextStore);
  private readonly reservations = inject(ResidentReservationService);

  readonly memberships = this.residentContext.memberships;
  readonly page = signal(1);
  readonly pageSize = signal(50);

  private readonly retrySubject = new Subject<void>();
  private readonly query = computed(() => ({
    buildingId: this.residentContext.activeMembership()?.buildingId ?? null,
    page: this.page(),
    pageSize: this.pageSize()
  }));

  protected readonly reservationsState = toSignal(
    merge(
      toObservable(this.query).pipe(
        distinctUntilChanged(
          (previous, current) =>
            previous.buildingId === current.buildingId &&
            previous.page === current.page &&
            previous.pageSize === current.pageSize
        )
      ),
      this.retrySubject.pipe(map(() => this.query()))
    ).pipe(
      switchMap((query) => {
        if (!query.buildingId) {
          return of<ReservationListState>({ status: 'idle' });
        }

        return this.reservations.list(query.buildingId, query.page, query.pageSize).pipe(
          map((reservationPage): ReservationListState =>
            reservationPage.items.length > 0
              ? { status: 'success', page: reservationPage }
              : { status: 'empty', page: reservationPage }
          ),
          catchError((error: ApiError) => of<ReservationListState>({ status: 'error', error })),
          startWith<ReservationListState>({ status: 'loading' })
        );
      })
    ),
    { initialValue: { status: 'idle' } as ReservationListState }
  );

  readonly reservationsPage = computed(() => {
    const state = this.reservationsState();
    return state.status === 'success' || state.status === 'empty' ? state.page : null;
  });

  readonly reservationsErrorTitle = computed(() => {
    const state = this.reservationsState();
    return state.status === 'error' ? state.error.title : '';
  });

  readonly reservationsErrorDetail = computed(() => {
    const state = this.reservationsState();
    return state.status === 'error' ? (state.error.detail ?? null) : null;
  });

  constructor() {
    effect(() => {
      this.residentContext.activeMembership()?.buildingId;
      untracked(() => this.page.set(1));
    });
  }

  onBuildingChange(event: CustomEvent<{ value: string }>): void {
    const buildingId = event.detail.value;
    if (buildingId) {
      this.residentContext.selectBuilding(buildingId);
    }
  }

  retryReservations(): void {
    this.retrySubject.next();
  }

  previousPage(): void {
    this.page.update((page) => Math.max(1, page - 1));
  }

  nextPage(): void {
    this.page.update((page) => page + 1);
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
