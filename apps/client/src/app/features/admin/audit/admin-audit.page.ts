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
import { AppErrorStateComponent } from '../../../shared/error-state/app-error-state.component';
import { AUDIT_ACTIONS, AUDIT_TARGET_TYPES, AuditFilters, AuditPage } from '../admin.models';
import { AdminService } from '../admin.service';

type AuditState =
  | { status: 'loading' }
  | { status: 'success'; page: AuditPage }
  | { status: 'empty'; page: AuditPage }
  | { status: 'error'; error: ApiError };

const pageSizes = [25, 50, 100] as const;

/**
 * Read-only view over the append-only audit trail (issue #54,
 * `GET /admin/audit` — `Modules/Audit/AuditEndpoints.cs`). There is no
 * mutation here: the trail cannot be edited, annotated or deleted from the
 * client, matching the backend, which exposes no such endpoint.
 *
 * Filter changes are race-safe: `reloadSubject` feeds a single `switchMap`
 * chain, so a fast filter change tears down the previous request's
 * subscription outright and a stale response can never overwrite a fresher
 * one (mirrors `AdminPaymentsPage`).
 */
@Component({
  selector: 'app-admin-audit-page',
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

      .admin-card pre {
        white-space: pre-wrap;
        word-break: break-word;
        background: var(--app-surface-variant, transparent);
        padding: var(--app-space-2);
      }
    `
  ],
  template: `
    <section aria-labelledby="admin-audit-title">
      <h1 id="admin-audit-title">Auditoría</h1>

      <form class="admin-filters" (submit)="applyFilters($event)">
        <ion-item>
          <ion-label position="stacked">ID del edificio</ion-label>
          <ion-input [value]="draft().buildingId ?? ''" (ionInput)="setDraft('buildingId', $event.detail.value)" />
        </ion-item>
        <ion-item>
          <ion-label position="stacked">ID de usuario del actor</ion-label>
          <ion-input [value]="draft().actorUserId ?? ''" (ionInput)="setDraft('actorUserId', $event.detail.value)" />
        </ion-item>
        <ion-item>
          <ion-label position="stacked">Acción</ion-label>
          <ion-select
            interface="popover"
            [value]="draft().action ?? ''"
            (ionChange)="setDraft('action', $event.detail.value)"
          >
            <ion-select-option value="">Todas</ion-select-option>
            @for (action of actions; track action) {
              <ion-select-option [value]="action">{{ action }}</ion-select-option>
            }
          </ion-select>
        </ion-item>
        <ion-item>
          <ion-label position="stacked">Tipo de objetivo</ion-label>
          <ion-select
            interface="popover"
            [value]="draft().targetType ?? ''"
            (ionChange)="setDraft('targetType', $event.detail.value)"
          >
            <ion-select-option value="">Todos</ion-select-option>
            @for (targetType of targetTypes; track targetType) {
              <ion-select-option [value]="targetType">{{ targetType }}</ion-select-option>
            }
          </ion-select>
        </ion-item>
        <ion-item>
          <ion-label position="stacked">ID del objetivo</ion-label>
          <ion-input [value]="draft().targetId ?? ''" (ionInput)="setDraft('targetId', $event.detail.value)" />
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
            <ion-spinner name="dots" /> <ion-text color="medium">Cargando auditoría…</ion-text>
          </p>
        }
        @case ('error') {
          <app-error-state
            [title]="errorTitle()"
            [detail]="errorDetail() ?? undefined"
            [showReintentar]="true"
            (retry)="reload()"
          />
        }
        @case ('empty') {
          <p><ion-text color="medium">No hay eventos de auditoría para estos filtros.</ion-text></p>
        }
        @case ('success') {
          <p>
            <ion-text color="medium">
              Página {{ page()!.page }} · {{ page()!.items.length }} de {{ page()!.totalCount }}
            </ion-text>
          </p>
          <ul class="admin-list" role="list">
            @for (item of page()!.items; track item.id) {
              <li class="admin-card">
                <h2>{{ item.action }}</h2>
                <div class="admin-card__meta">
                  <span>{{ item.occurredAtUtc }}</span>
                  <span>Actor: {{ item.actorType }}{{ item.actorUserId ? ' · ' + item.actorUserId : '' }}</span>
                  <span>Target: {{ item.targetType }}{{ item.targetId ? ' · ' + item.targetId : '' }}</span>
                  @if (item.buildingId) {
                    <span>Building: {{ item.buildingId }}</span>
                  }
                  @if (item.correlationId) {
                    <span>Correlation: {{ item.correlationId }}</span>
                  }
                </div>
                @if (item.metadata !== null && item.metadata !== undefined) {
                  <pre>{{ formatMetadata(item.metadata) }}</pre>
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
        <ion-button type="button" fill="outline" [disabled]="!hasSiguientePage()" (click)="nextPage()">Siguiente</ion-button>
      </div>
    </section>
  `
})
export class AdminAuditPage {
  private readonly admin = inject(AdminService);
  private readonly reloadSubject = new Subject<AuditFilters>();

  protected readonly actions = AUDIT_ACTIONS;
  protected readonly targetTypes = AUDIT_TARGET_TYPES;
  protected readonly sizes = pageSizes;

  readonly filters = signal<AuditFilters>({ page: 1, pageSize: 50 });
  readonly draft = signal<AuditFilters>({ page: 1, pageSize: 50 });

  protected readonly state = signal<AuditState>({ status: 'loading' });
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
  protected readonly hasSiguientePage = computed(() => {
    const page = this.page();
    return page ? page.page * page.pageSize < page.totalCount : false;
  });

  constructor() {
    this.reloadSubject
      .pipe(
        switchMap((filters) =>
          this.admin.listAudit(filters).pipe(
            map((page): AuditState =>
              page.items.length > 0 ? { status: 'success', page } : { status: 'empty', page }
            ),
            catchError((error: ApiError) => of<AuditState>({ status: 'error', error })),
            startWith<AuditState>({ status: 'loading' })
          )
        )
      )
      .subscribe((state) => this.state.set(state));

    this.reload();
  }

  setDraft(key: keyof AuditFilters, value: unknown): void {
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
    if (this.hasSiguientePage()) {
      this.changePage(this.filters().page + 1);
    }
  }

  formatMetadata(metadata: unknown): string {
    return JSON.stringify(metadata, null, 2);
  }

  private changePage(page: number): void {
    const next = { ...this.filters(), page };
    this.filters.set(next);
    this.draft.set(next);
    this.reload();
  }
}
