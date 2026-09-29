import { Component, computed, inject, signal } from '@angular/core';
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
import { AdminPaymentFilters, AdminPaymentPage } from '../admin.models';
import { AdminService } from '../admin.service';

type PaymentsState =
  | { status: 'loading' }
  | { status: 'success'; page: AdminPaymentPage }
  | { status: 'empty'; page: AdminPaymentPage }
  | { status: 'error'; error: ApiError };

const methods = ['MercadoPago', 'Cash'] as const;
const statuses = ['Created', 'Pending', 'Approved', 'Rejected', 'Cancelled'] as const;
const pageSizes = [25, 50, 100] as const;

@Component({
  selector: 'app-admin-payments-page',
  standalone: true,
  imports: [
    IonButton,
    IonInput,
    IonItem,
    IonLabel,
    IonSelect,
    IonSelectOption,
    IonSpinner,
    IonText
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
    <section aria-labelledby="admin-payments-title">
      <h1 id="admin-payments-title">Pagos</h1>

      <form class="admin-filters" (submit)="applyFilters($event)">
        <ion-item>
          <ion-label position="stacked">Building ID</ion-label>
          <ion-input [value]="draft().buildingId ?? ''" (ionInput)="setDraft('buildingId', $event.detail.value)" />
        </ion-item>
        <ion-item>
          <ion-label position="stacked">Method</ion-label>
          <ion-select
            interface="popover"
            [value]="draft().method ?? ''"
            (ionChange)="setDraft('method', $event.detail.value)"
          >
            <ion-select-option value="">Todos</ion-select-option>
            @for (method of methods; track method) {
              <ion-select-option [value]="method">{{ method }}</ion-select-option>
            }
          </ion-select>
        </ion-item>
        <ion-item>
          <ion-label position="stacked">Status</ion-label>
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
          <ion-label position="stacked">Manual review</ion-label>
          <ion-select
            interface="popover"
            [value]="manualReviewValue()"
            (ionChange)="setManualReview($event.detail.value)"
          >
            <ion-select-option value="">Todos</ion-select-option>
            <ion-select-option value="true">Sí</ion-select-option>
            <ion-select-option value="false">No</ion-select-option>
          </ion-select>
        </ion-item>
        <ion-item>
          <ion-label position="stacked">Reservation ID</ion-label>
          <ion-input [value]="draft().reservationId ?? ''" (ionInput)="setDraft('reservationId', $event.detail.value)" />
        </ion-item>
        <ion-item>
          <ion-label position="stacked">From UTC</ion-label>
          <ion-input [value]="draft().fromUtc ?? ''" (ionInput)="setDraft('fromUtc', $event.detail.value)" />
        </ion-item>
        <ion-item>
          <ion-label position="stacked">To UTC</ion-label>
          <ion-input [value]="draft().toUtc ?? ''" (ionInput)="setDraft('toUtc', $event.detail.value)" />
        </ion-item>
        <ion-item>
          <ion-label position="stacked">Page size</ion-label>
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
            <ion-spinner name="dots" /> <ion-text color="medium">Cargando pagos…</ion-text>
          </p>
        }
        @case ('error') {
          <p role="alert"><ion-text color="danger">{{ errorTitle() }}</ion-text></p>
          <ion-button type="button" fill="outline" (click)="reload()">Reintentar</ion-button>
        }
        @case ('empty') {
          <p><ion-text color="medium">No hay pagos para estos filtros.</ion-text></p>
        }
        @case ('success') {
          <p>
            <ion-text color="medium">
              Página {{ page()!.page }} · {{ page()!.items.length }} de {{ page()!.totalCount }}
            </ion-text>
          </p>
          <ul class="admin-list" role="list">
            @for (payment of page()!.items; track payment.paymentId) {
              <li class="admin-card">
                <h2>{{ payment.method }} · {{ payment.status }}</h2>
                <div class="admin-card__meta">
                  <span>{{ payment.amount }} {{ payment.currency }}</span>
                  <span>{{ payment.createdAtUtc }}</span>
                  <span>Reservation {{ payment.reservationId }}</span>
                  @if (payment.requiresManualReview) {
                    <ion-text color="warning">Requiere revisión administrativa</ion-text>
                  }
                </div>
                <p>Outcome: {{ payment.reservationOutcome }}</p>
                @if (payment.providerStatus) {
                  <p>Provider: {{ payment.providerStatus }} {{ payment.providerStatusDetail ?? '' }}</p>
                }
                @if (payment.cashDeclaredAtUtc) {
                  <p>Efectivo declarado: {{ payment.cashDeclaredAtUtc }}</p>
                }
                @if (payment.cashConfirmedAtUtc) {
                  <p>Efectivo confirmado: {{ payment.cashConfirmedAtUtc }}</p>
                }
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
export class AdminPaymentsPage {
  private readonly admin = inject(AdminService);
  private readonly reloadSubject = new Subject<AdminPaymentFilters>();

  protected readonly methods = methods;
  protected readonly statuses = statuses;
  protected readonly sizes = pageSizes;

  readonly filters = signal<AdminPaymentFilters>({ page: 1, pageSize: 50 });
  readonly draft = signal<AdminPaymentFilters>({ page: 1, pageSize: 50 });

  protected readonly state = signal<PaymentsState>({ status: 'loading' });
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
  protected readonly manualReviewValue = computed(() => {
    const value = this.draft().requiresManualReview;
    return value === undefined ? '' : String(value);
  });

  constructor() {
    this.reloadSubject
      .pipe(
        switchMap((filters) =>
          this.admin.listPayments(filters).pipe(
            map((page): PaymentsState =>
              page.items.length > 0 ? { status: 'success', page } : { status: 'empty', page }
            ),
            catchError((error: ApiError) => of<PaymentsState>({ status: 'error', error })),
            startWith<PaymentsState>({ status: 'loading' })
          )
        )
      )
      .subscribe((state) => this.state.set(state));

    this.reload();
  }

  setDraft(key: keyof AdminPaymentFilters, value: unknown): void {
    const normalized = value === '' ? undefined : value;
    this.draft.update((current) => ({ ...current, [key]: normalized }));
  }

  setManualReview(value: string): void {
    const requiresManualReview = value === '' ? undefined : value === 'true';
    this.draft.update((current) => ({ ...current, requiresManualReview }));
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
