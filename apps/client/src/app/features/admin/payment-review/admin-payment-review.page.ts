import { Component, computed, inject, signal } from '@angular/core';
import { IonButton, IonInput, IonItem, IonLabel, IonSpinner, IonText } from '@ionic/angular';
import { Subject, catchError, map, of, startWith, switchMap } from 'rxjs';

import { ApiError } from '../../../core/api/api-error';
import { AdminPayment, AdminPaymentFilters, AdminPaymentPage } from '../admin.models';
import { AdminService } from '../admin.service';

type ReviewState =
  | { status: 'loading' }
  | { status: 'success'; page: AdminPaymentPage }
  | { status: 'empty'; page: AdminPaymentPage }
  | { status: 'error'; error: ApiError };

/**
 * Filters this page lets an Administrator narrow by, on top of the fixed
 * `requiresManualReview: true` (see {@link toFilters}).
 */
interface ReviewFilterDraft {
  buildingId?: string;
  reservationId?: string;
  fromUtc?: string;
  toUtc?: string;
  page: number;
  pageSize: number;
}

const pageSizes = [25, 50, 100] as const;

/**
 * The three approved-payment outcomes that flag `requiresManualReview` —
 * copied verbatim from `PaymentReservationOutcome` (backend) /
 * `PaymentReservationOutcome` (`payment.models.ts`, issue #49). This page
 * only ever explains what already happened; it never suggests or performs a
 * resolution, because no such backend capability exists.
 */
const MANUAL_REVIEW_OUTCOME_EXPLANATIONS: Record<string, string> = {
  ApprovedAfterExpiry: 'El pago se acreditó después de que venciera el plazo de la reserva.',
  ApprovedForCancelledReservation: 'El pago se acreditó pero la reserva ya estaba cancelada.',
  ApprovedForMissingReservation: 'El pago se acreditó pero no se encontró la reserva asociada.'
};

/**
 * Read-only manual-review queue (issue #54): payments the backend already
 * flagged with `requiresManualReview: true` when reconciling Mercado Pago or
 * confirming cash (`PaymentReservationOutcomeMapper`). This reuses
 * `AdminService.listPayments` with that one fixed filter — there is
 * deliberately no separate backend endpoint for "manual review payments".
 *
 * This view renders no resolve/approve/refund/dismiss action of any kind.
 * The backend exposes no such capability for these payments — an Approved
 * payment is a legal fact, not a proposal — so the client must not imply
 * one exists.
 */
@Component({
  selector: 'app-admin-payment-review-page',
  standalone: true,
  imports: [IonButton, IonInput, IonItem, IonLabel, IonSpinner, IonText],
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
    <section aria-labelledby="admin-payment-review-title">
      <h1 id="admin-payment-review-title">Pagos con revisión manual</h1>
      <p>
        <ion-text color="medium">
          Estos pagos fueron aprobados por el proveedor o por efectivo, pero el backend detectó una
          situación que amerita revisión (por ejemplo, la reserva ya no estaba disponible). Esta
          vista es de solo lectura: no existe ninguna acción de resolución, aprobación o reembolso
          para estos pagos.
        </ion-text>
      </p>

      <form class="admin-filters" (submit)="applyFilters($event)">
        <ion-item>
          <ion-label position="stacked">Building ID</ion-label>
          <ion-input [value]="draft().buildingId ?? ''" (ionInput)="setDraft('buildingId', $event.detail.value)" />
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
        <ion-button type="submit">Aplicar</ion-button>
      </form>

      @switch (state().status) {
        @case ('loading') {
          <p aria-live="polite">
            <ion-spinner name="dots" /> <ion-text color="medium">Cargando pagos…</ion-text>
          </p>
        }
        @case ('error') {
          <p role="alert">
            <ion-text color="danger">{{ errorTitle() }}</ion-text>
            @if (errorDetail(); as detail) {
              <br />
              <ion-text color="danger">{{ detail }}</ion-text>
            }
          </p>
          <ion-button type="button" fill="outline" (click)="reload()">Reintentar</ion-button>
        }
        @case ('empty') {
          <p><ion-text color="medium">No hay pagos que requieran revisión manual.</ion-text></p>
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
                  <span>Payment {{ payment.paymentId }}</span>
                  <span>Reservation {{ payment.reservationId }}</span>
                  <span>{{ payment.amount }} {{ payment.currency }}</span>
                  <span>Creado: {{ payment.createdAtUtc }}</span>
                  @if (payment.approvedAtUtc) {
                    <span>Aprobado: {{ payment.approvedAtUtc }}</span>
                  }
                </div>
                <p>
                  Outcome: <strong>{{ payment.reservationOutcome }}</strong>
                  @if (outcomeExplanation(payment); as explanation) {
                    — {{ explanation }}
                  }
                </p>
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
export class AdminPaymentReviewPage {
  private readonly admin = inject(AdminService);
  private readonly reloadSubject = new Subject<ReviewFilterDraft>();

  protected readonly sizes = pageSizes;

  readonly filters = signal<ReviewFilterDraft>({ page: 1, pageSize: 50 });
  readonly draft = signal<ReviewFilterDraft>({ page: 1, pageSize: 50 });

  protected readonly state = signal<ReviewState>({ status: 'loading' });
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

  constructor() {
    this.reloadSubject
      .pipe(
        switchMap((draft) =>
          this.admin.listPayments(toFilters(draft)).pipe(
            map((page): ReviewState =>
              page.items.length > 0 ? { status: 'success', page } : { status: 'empty', page }
            ),
            catchError((error: ApiError) => of<ReviewState>({ status: 'error', error })),
            startWith<ReviewState>({ status: 'loading' })
          )
        )
      )
      .subscribe((state) => this.state.set(state));

    this.reload();
  }

  setDraft(key: keyof ReviewFilterDraft, value: unknown): void {
    const normalized = value === '' ? undefined : value;
    this.draft.update((current) => ({ ...current, [key]: normalized }));
  }

  applyFilters(event: Event): void {
    event.preventDefault();
    const next = { ...this.draft(), page: 1 };
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

  outcomeExplanation(payment: AdminPayment): string | null {
    return MANUAL_REVIEW_OUTCOME_EXPLANATIONS[payment.reservationOutcome] ?? null;
  }

  private changePage(page: number): void {
    const next = { ...this.filters(), page };
    this.filters.set(next);
    this.draft.set(next);
    this.reload();
  }
}

function toFilters(draft: ReviewFilterDraft): AdminPaymentFilters {
  return { ...draft, requiresManualReview: true };
}
