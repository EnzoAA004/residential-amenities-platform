import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
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
import { AppErrorStateComponent } from '../../../shared/error-state/app-error-state.component';
import {
  AdminPayment,
  AdminPaymentFilters,
  AdminPaymentPage,
  ConfirmCashPaymentResponse
} from '../admin.models';
import { AdminService } from '../admin.service';

type PaymentsState =
  | { status: 'loading' }
  | { status: 'success'; page: AdminPaymentPage }
  | { status: 'empty'; page: AdminPaymentPage }
  | { status: 'error'; error: ApiError };

/**
 * Cash-confirmation state for the payment list, keyed by `paymentId`. Only
 * one confirmation can be in flight/armed at a time — confirming cash
 * receipt is a financial mutation, so it always requires an explicit
 * "Sí, confirmar" step (`'armed'`) before the POST fires, and the UI blocks
 * a second submit while `'confirming'`.
 */
type CashConfirmState =
  | { status: 'idle' }
  | { status: 'armed'; paymentId: string }
  | { status: 'confirming'; paymentId: string }
  | { status: 'error'; paymentId: string; error: ApiError };

const methods = ['MercadoPago', 'Cash'] as const;
const statuses = ['Created', 'Pending', 'Approved', 'Rejected', 'Cancelled'] as const;
const pageSizes = [25, 50, 100] as const;

@Component({
  selector: 'app-admin-payments-page',
  standalone: true,
  imports: [
    AppErrorStateComponent,
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
          <ion-label position="stacked">ID del edificio</ion-label>
          <ion-input [value]="draft().buildingId ?? ''" (ionInput)="setDraft('buildingId', $event.detail.value)" />
        </ion-item>
        <ion-item>
          <ion-label position="stacked">Método</ion-label>
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
          <ion-label position="stacked">Revisión manual</ion-label>
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
          <ion-label position="stacked">ID de reserva</ion-label>
          <ion-input [value]="draft().reservationId ?? ''" (ionInput)="setDraft('reservationId', $event.detail.value)" />
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
            <ion-spinner name="dots" /> <ion-text color="medium">Cargando pagos…</ion-text>
          </p>
        }
        @case ('error') {
          <app-error-state
            [title]="errorTitle()"
            [detail]="errorDetail() ?? undefined"
            [showRetry]="true"
            (retry)="reload()"
          />
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
                  <span>Reserva {{ payment.reservationId }}</span>
                  @if (payment.requiresManualReview) {
                    <ion-text color="warning">Requiere revisión administrativa</ion-text>
                  }
                </div>
                <p>Resultado: {{ payment.reservationOutcome }}</p>
                @if (payment.providerStatus) {
                  <p>Proveedor: {{ payment.providerStatus }} {{ payment.providerStatusDetail ?? '' }}</p>
                }
                @if (payment.cashDeclaredAtUtc) {
                  <p>Efectivo declarado: {{ payment.cashDeclaredAtUtc }}</p>
                }
                @if (payment.cashConfirmedAtUtc) {
                  <p>Efectivo confirmado: {{ payment.cashConfirmedAtUtc }}</p>
                }

                @if (canConfirmCash(payment)) {
                  @if (isArmed(payment.paymentId)) {
                    <p role="alert">
                      <ion-text color="warning">¿Confirmar que se recibió el efectivo de este pago?</ion-text>
                    </p>
                    <div class="admin-card__meta">
                      <ion-button
                        type="button"
                        color="warning"
                        [disabled]="isConfirming(payment.paymentId)"
                        (click)="confirmCash(payment.paymentId)"
                      >
                        {{ isConfirming(payment.paymentId) ? 'Confirmando…' : 'Sí, confirmar' }}
                      </ion-button>
                      <ion-button
                        type="button"
                        fill="outline"
                        [disabled]="isConfirming(payment.paymentId)"
                        (click)="cancelConfirmCash()"
                      >
                        Cancelar
                      </ion-button>
                    </div>
                  } @else {
                    <ion-button type="button" fill="outline" (click)="armConfirmCash(payment.paymentId)">
                      Confirmar efectivo
                    </ion-button>
                  }

                  @if (confirmErrorFor(payment.paymentId); as confirmError) {
                    <app-error-state [title]="confirmError.title" [detail]="confirmError.detail" />
                  }
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
  private readonly destroyRef = inject(DestroyRef);
  private readonly reloadSubject = new Subject<AdminPaymentFilters>();

  protected readonly confirmState = signal<CashConfirmState>({ status: 'idle' });

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
  protected readonly errorDetail = computed(() => {
    const state = this.state();
    return state.status === 'error' ? (state.error.detail ?? null) : null;
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

  canConfirmCash(payment: AdminPayment): boolean {
    return payment.method === 'Cash' && payment.status === 'Pending';
  }

  isArmed(paymentId: string): boolean {
    const state = this.confirmState();
    return state.status === 'armed' && state.paymentId === paymentId;
  }

  isConfirming(paymentId: string): boolean {
    const state = this.confirmState();
    return state.status === 'confirming' && state.paymentId === paymentId;
  }

  confirmErrorFor(paymentId: string): ApiError | null {
    const state = this.confirmState();
    return state.status === 'error' && state.paymentId === paymentId ? state.error : null;
  }

  armConfirmCash(paymentId: string): void {
    if (this.confirmState().status === 'confirming') {
      return;
    }

    this.confirmState.set({ status: 'armed', paymentId });
  }

  cancelConfirmCash(): void {
    if (this.confirmState().status === 'confirming') {
      return;
    }

    this.confirmState.set({ status: 'idle' });
  }

  confirmCash(paymentId: string): void {
    if (this.confirmState().status === 'confirming') {
      return;
    }

    this.confirmState.set({ status: 'confirming', paymentId });

    this.admin
      .confirmCashPayment(paymentId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (response) => {
          this.applyCashConfirmation(response);
          this.confirmState.set({ status: 'idle' });
        },
        error: (error: ApiError) => {
          this.confirmState.set({ status: 'error', paymentId, error });
        }
      });
  }

  /**
   * Merges the confirm response's real fields into the currently rendered
   * page — never assumes `Approved` implies `ReservationConfirmed`, always
   * takes `reservationOutcome`/`requiresManualReview` from the response.
   */
  private applyCashConfirmation(response: ConfirmCashPaymentResponse): void {
    this.state.update((current) => {
      if (current.status !== 'success' && current.status !== 'empty') {
        return current;
      }

      const items = current.page.items.map((item): AdminPayment =>
        item.paymentId === response.paymentId
          ? {
              ...item,
              status: response.status,
              amount: response.amount,
              currency: response.currency,
              cashConfirmedAtUtc: response.cashConfirmedAtUtc,
              cashConfirmedByUserId: response.cashConfirmedByUserId,
              reservationOutcome: response.reservationOutcome,
              requiresManualReview: response.requiresManualReview
            }
          : item
      );

      return { ...current, page: { ...current.page, items } };
    });
  }
}
