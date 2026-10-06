import { Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import {
  IonButton,
  IonInput,
  IonItem,
  IonLabel,
  IonSelect,
  IonSelectOption,
  IonSpinner,
  IonText
} from '@ionic/angular';
import { Subject, catchError, map, of, startWith, switchMap } from 'rxjs';

import { ApiError } from '../../../core/api/api-error';
import { AdminReservationFilters, AdminReservationPage } from '../admin.models';
import { AdminService } from '../admin.service';

type ReservationsState =
  | { status: 'loading' }
  | { status: 'success'; page: AdminReservationPage }
  | { status: 'empty'; page: AdminReservationPage }
  | { status: 'error'; error: ApiError };

const reservationStatuses = ['Pending', 'Confirmed', 'Cancelled', 'Expired'] as const;
const reservationUseTypes = ['SharedLeisure', 'ExclusiveLeisure', 'Event'] as const;
const pageSizes = [25, 50, 100] as const;

@Component({
  selector: 'app-admin-reservations-page',
  standalone: true,
  imports: [
    IonButton,
    IonInput,
    IonItem,
    IonLabel,
    IonSelect,
    IonSelectOption,
    IonSpinner,
    IonText,
    RouterLink
  ],
  styles: [
    `
      .admin-filters,
      .admin-list,
      .admin-pagination {
        display: grid;
        gap: var(--app-space-3);
      }

      .admin-filters {
        grid-template-columns: repeat(auto-fit, minmax(220px, 1fr));
        margin-block-end: var(--app-space-4);
      }

      .admin-list {
        margin: 0;
        padding: 0;
        list-style: none;
      }

      .admin-card {
        border: 1px solid var(--app-border-color);
        border-radius: var(--app-radius-md);
        padding: var(--app-space-3);
        background: var(--app-surface);
      }

      .admin-card__meta,
      .admin-pagination {
        display: flex;
        flex-wrap: wrap;
        gap: var(--app-space-2);
        align-items: center;
      }
    `
  ],
  template: `
    <section aria-labelledby="admin-reservations-title">
      <h1 id="admin-reservations-title">Reservas</h1>

      <form class="admin-filters" (submit)="applyFilters($event)">
        <ion-item>
          <ion-label position="stacked">ID del edificio</ion-label>
          <ion-input [value]="draft().buildingId ?? ''" (ionInput)="setDraft('buildingId', $event.detail.value)" />
        </ion-item>
        <ion-item>
          <ion-label position="stacked">Estado</ion-label>
          <ion-select
            interface="popover"
            [value]="draft().status ?? ''"
            (ionChange)="setDraft('status', $event.detail.value)"
          >
            <ion-select-option value="">Todos</ion-select-option>
            @for (status of statuses; track status) {
              <ion-select-option [value]="status">{{ status }}</ion-select-option>
            }
          </ion-select>
        </ion-item>
        <ion-item>
          <ion-label position="stacked">Tipo de uso</ion-label>
          <ion-select
            interface="popover"
            [value]="draft().useType ?? ''"
            (ionChange)="setDraft('useType', $event.detail.value)"
          >
            <ion-select-option value="">Todos</ion-select-option>
            @for (useType of useTypes; track useType) {
              <ion-select-option [value]="useType">{{ useType }}</ion-select-option>
            }
          </ion-select>
        </ion-item>
        <ion-item>
          <ion-label position="stacked">Desde (UTC)</ion-label>
          <ion-input [value]="draft().fromUtc ?? ''" (ionInput)="setDraft('fromUtc', $event.detail.value)" />
        </ion-item>
        <ion-item>
          <ion-label position="stacked">Hasta (UTC)</ion-label>
          <ion-input [value]="draft().toUtc ?? ''" (ionInput)="setDraft('toUtc', $event.detail.value)" />
        </ion-item>
        <ion-item>
          <ion-label position="stacked">Membership ID</ion-label>
          <ion-input [value]="draft().membershipId ?? ''" (ionInput)="setDraft('membershipId', $event.detail.value)" />
        </ion-item>
        <ion-item>
          <ion-label position="stacked">Tamaño de página</ion-label>
          <ion-select
            interface="popover"
            [value]="draft().pageSize"
            (ionChange)="setDraft('pageSize', $event.detail.value)"
          >
            @for (size of sizes; track size) {
              <ion-select-option [value]="size">{{ size }}</ion-select-option>
            }
          </ion-select>
        </ion-item>
        <ion-button type="submit">Aplicar</ion-button>
      </form>

      @switch (state().status) {
        @case ('loading') {
          <p aria-live="polite">
            <ion-spinner name="dots" /> <ion-text color="medium">Cargando reservas…</ion-text>
          </p>
        }
        @case ('error') {
          <p role="alert"><ion-text color="danger">{{ errorTitle() }}</ion-text></p>
          <ion-button type="button" fill="outline" (click)="reload()">Reintentar</ion-button>
        }
        @case ('empty') {
          <p><ion-text color="medium">No hay reservas para estos filtros.</ion-text></p>
        }
        @case ('success') {
          <p>
            <ion-text color="medium">
              Página {{ page()!.page }} · {{ page()!.items.length }} de {{ page()!.totalCount }}
            </ion-text>
          </p>
          <ul class="admin-list" role="list">
            @for (item of page()!.items; track item.reservation.reservationId) {
              <li class="admin-card">
                <h2>{{ item.reservation.useType }} · {{ item.reservation.status }}</h2>
                <div class="admin-card__meta">
                  <span>{{ item.reservation.startsAtUtc }} → {{ item.reservation.endsAtUtc }}</span>
                  <span>{{ item.reservation.total }} {{ item.reservation.currency ?? '' }}</span>
                  <span>{{ item.payments.length }} pagos</span>
                  @if (item.requiresFinancialReview) {
                    <ion-text color="warning">Requiere revisión financiera</ion-text>
                  }
                </div>
                <p>
                  <ion-text color="medium">Edificio {{ item.reservation.buildingId }}</ion-text>
                </p>
                <ion-button [routerLink]="['/admin/reservations', item.reservation.reservationId]" fill="outline">
                  Ver detalle
                </ion-button>
              </li>
            }
          </ul>
        }
      }

      <div class="admin-pagination" aria-label="Paginación">
        <ion-button type="button" fill="outline" [disabled]="filters().page <= 1" (click)="previousPage()">
          Anterior
        </ion-button>
        <ion-button type="button" fill="outline" [disabled]="!hasNextPage()" (click)="nextPage()">Siguiente</ion-button>
      </div>
    </section>
  `
})
export class AdminReservationsPage {
  private readonly admin = inject(AdminService);
  private readonly reloadSubject = new Subject<AdminReservationFilters>();

  protected readonly statuses = reservationStatuses;
  protected readonly useTypes = reservationUseTypes;
  protected readonly sizes = pageSizes;

  readonly filters = signal<AdminReservationFilters>({ page: 1, pageSize: 50 });
  readonly draft = signal<AdminReservationFilters>({ page: 1, pageSize: 50 });

  protected readonly state = signal<ReservationsState>({ status: 'loading' });
  protected readonly page = computed(() => {
    const state = this.state();
    return state.status === 'success' || state.status === 'empty' ? state.page : null;
  });
  protected readonly errorTitle = computed(() => {
    const state = this.state();
    return state.status === 'error' ? state.error.title : '';
  });
  protected readonly hasNextPage = computed(() => {
    const page = this.page();
    return page ? page.page * page.pageSize < page.totalCount : false;
  });

  constructor() {
    this.reloadSubject
      .pipe(
        switchMap((filters) =>
          this.admin.listReservations(filters).pipe(
            map((page): ReservationsState =>
              page.items.length > 0 ? { status: 'success', page } : { status: 'empty', page }
            ),
            catchError((error: ApiError) => of<ReservationsState>({ status: 'error', error })),
            startWith<ReservationsState>({ status: 'loading' })
          )
        )
      )
      .subscribe((state) => this.state.set(state));

    this.reload();
  }

  setDraft(key: keyof AdminReservationFilters, value: unknown): void {
    const normalized = value === '' ? undefined : value;
    this.draft.update((current) => ({ ...current, [key]: normalized }));
  }

  applyFilters(event: Event): void {
    event.preventDefault();
    const next = { ...this.draft(), page: 1, pageSize: Number(this.draft().pageSize) };
    this.filters.set(next);
    this.draft.set(next);
    this.reload();
  }

  reload(): void {
    this.reloadSubject.next(this.filters());
  }

  previousPage(): void {
    this.changePage(Math.max(1, this.filters().page - 1));
  }

  nextPage(): void {
    if (this.hasNextPage()) {
      this.changePage(this.filters().page + 1);
    }
  }

  private changePage(page: number): void {
    const next = { ...this.filters(), page };
    this.filters.set(next);
    this.draft.set(next);
    this.reload();
  }
}
