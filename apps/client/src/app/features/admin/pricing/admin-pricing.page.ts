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
import {
  AdminPriceComponentType,
  AdminPriceRuleFilters,
  AdminPriceRulePage,
  AdminReservationUseType,
  CreatePriceRuleRequest,
  PriceRuleCreationResult
} from '../admin.models';
import { AdminService } from '../admin.service';

type ListState =
  | { status: 'idle' }
  | { status: 'loading' }
  | { status: 'success'; page: AdminPriceRulePage }
  | { status: 'empty'; page: AdminPriceRulePage }
  | { status: 'error'; error: ApiError };

type CreateState =
  | { status: 'idle' }
  | { status: 'creating' }
  | { status: 'created'; result: PriceRuleCreationResult }
  | { status: 'error'; error: ApiError };

interface CreateDraft {
  buildingId: string;
  amenityId: string;
  componentType: AdminPriceComponentType;
  useType: AdminReservationUseType;
  currency: string;
  amount: string;
  effectiveFromUtc: string;
  effectiveToUtc: string;
}

const componentTypes: AdminPriceComponentType[] = ['Base', 'AddOn'];
const useTypes: AdminReservationUseType[] = ['SharedLeisure', 'ExclusiveLeisure', 'Event'];
const pageSizes = [25, 50, 100] as const;

function emptyDraft(): CreateDraft {
  return {
    buildingId: '',
    amenityId: '',
    componentType: 'Base',
    useType: 'SharedLeisure',
    currency: '',
    amount: '',
    effectiveFromUtc: '',
    effectiveToUtc: ''
  };
}

/**
 * Administrator price-rule configuration (issue #53). Rules are
 * effective-dated: creating one never edits an existing rule, it supersedes
 * it (closes its `effectiveToUtc`). The UI must always show both the created
 * rule and any superseded ones, and never call this "editing".
 */
@Component({
  selector: 'app-admin-pricing-page',
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
      .admin-create,
      .admin-list,
      .admin-pagination {
        display: grid;
        gap: var(--app-space-3);
      }

      .admin-filters,
      .admin-create {
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

      .admin-pagination {
        display: flex;
        flex-wrap: wrap;
        gap: var(--app-space-2);
        align-items: center;
      }

      .superseded-list {
        margin: var(--app-space-2) 0 0;
        padding-inline-start: var(--app-space-4);
      }
    `
  ],
  template: `
    <section aria-labelledby="admin-pricing-title">
      <h1 id="admin-pricing-title">Reglas de precio</h1>

      <form class="admin-filters" (submit)="applyFilters($event)">
        <ion-item>
          <ion-label position="stacked">Building ID (requerido)</ion-label>
          <ion-input [value]="draftBuildingId()" (ionInput)="draftBuildingId.set($event.detail.value ?? '')" />
        </ion-item>
        <ion-item>
          <ion-label position="stacked">Amenity ID (opcional)</ion-label>
          <ion-input [value]="draftAmenityId()" (ionInput)="draftAmenityId.set($event.detail.value ?? '')" />
        </ion-item>
        <ion-item>
          <ion-label position="stacked">Activa en (UTC, opcional)</ion-label>
          <ion-input
            [value]="draftActiveAtUtc()"
            (ionInput)="draftActiveAtUtc.set($event.detail.value ?? '')"
            placeholder="p. ej. 2026-10-01T00:00:00Z"
          />
        </ion-item>
        <ion-item>
          <ion-label position="stacked">Page size</ion-label>
          <ion-select
            interface="popover"
            [value]="draftPageSize()"
            (ionChange)="draftPageSize.set($event.detail.value)"
          >
            @for (size of sizes; track size) {
              <ion-select-option [value]="size">{{ size }}</ion-select-option>
            }
          </ion-select>
        </ion-item>
        <ion-button type="submit">Buscar</ion-button>
      </form>

      @switch (listState().status) {
        @case ('idle') {
          <p><ion-text color="medium">Ingresá un Building ID y buscá para ver las reglas.</ion-text></p>
        }
        @case ('loading') {
          <p aria-live="polite">
            <ion-spinner name="dots" /> <ion-text color="medium">Cargando reglas…</ion-text>
          </p>
        }
        @case ('error') {
          <app-error-state [title]="listErrorTitle()" [detail]="listErrorDetail() ?? undefined" />
        }
        @case ('empty') {
          <p><ion-text color="medium">No hay reglas de precio para estos filtros.</ion-text></p>
        }
        @case ('success') {
          <p>
            <ion-text color="medium">
              Página {{ page()!.page }} · {{ page()!.items.length }} de {{ page()!.totalCount }}
            </ion-text>
          </p>
          <ul class="admin-list" role="list">
            @for (rule of page()!.items; track rule.id) {
              <li class="admin-card">
                <h2>{{ rule.useType }} · {{ rule.componentType }}</h2>
                <p>{{ rule.amount }} {{ rule.currency }}</p>
                <p>
                  <ion-text color="medium">
                    Vigente desde {{ rule.effectiveFromUtc }}
                    @if (rule.effectiveToUtc) {
                      hasta {{ rule.effectiveToUtc }}
                    } @else {
                      (sin fin definido)
                    }
                  </ion-text>
                </p>
                <p><ion-text color="medium">Amenity {{ rule.amenityId }}</ion-text></p>
              </li>
            }
          </ul>

          <div class="admin-pagination" aria-label="Paginación">
            <ion-button type="button" fill="outline" [disabled]="filters()!.page <= 1" (click)="previousPage()">
              Anterior
            </ion-button>
            <ion-button type="button" fill="outline" [disabled]="!hasNextPage()" (click)="nextPage()">
              Siguiente
            </ion-button>
          </div>
        }
      }

      <h2>Crear regla de precio</h2>
      <p>
        <ion-text color="medium">
          Una nueva regla nunca edita una existente: reemplaza (supersede) a la regla vigente para la misma
          combinación de amenity, componente y tipo de uso a partir de su fecha de vigencia.
        </ion-text>
      </p>

      <form class="admin-create" (submit)="submitCreate($event)">
        <ion-item>
          <ion-label position="stacked">Building ID</ion-label>
          <ion-input [value]="createDraft().buildingId" (ionInput)="setCreateField('buildingId', $event.detail.value)" />
        </ion-item>
        <ion-item>
          <ion-label position="stacked">Amenity ID</ion-label>
          <ion-input [value]="createDraft().amenityId" (ionInput)="setCreateField('amenityId', $event.detail.value)" />
        </ion-item>
        <ion-item>
          <ion-label position="stacked">Componente</ion-label>
          <ion-select
            interface="popover"
            [value]="createDraft().componentType"
            (ionChange)="setCreateField('componentType', $event.detail.value)"
          >
            @for (type of componentTypes; track type) {
              <ion-select-option [value]="type">{{ type }}</ion-select-option>
            }
          </ion-select>
        </ion-item>
        <ion-item>
          <ion-label position="stacked">Tipo de uso</ion-label>
          <ion-select
            interface="popover"
            [value]="createDraft().useType"
            (ionChange)="setCreateField('useType', $event.detail.value)"
          >
            @for (type of useTypesOptions; track type) {
              <ion-select-option [value]="type">{{ type }}</ion-select-option>
            }
          </ion-select>
        </ion-item>
        <ion-item>
          <ion-label position="stacked">Moneda (ISO 3 letras)</ion-label>
          <ion-input
            [value]="createDraft().currency"
            (ionInput)="setCreateField('currency', $event.detail.value)"
            placeholder="p. ej. ARS"
            maxlength="3"
          />
        </ion-item>
        <ion-item>
          <ion-label position="stacked">Monto</ion-label>
          <ion-input
            type="number"
            [value]="createDraft().amount"
            (ionInput)="setCreateField('amount', $event.detail.value)"
            placeholder="Ejemplo: 0.01 (no es un precio real)"
          />
        </ion-item>
        <ion-item>
          <ion-label position="stacked">Vigente desde (UTC, opcional; por defecto ahora)</ion-label>
          <ion-input
            [value]="createDraft().effectiveFromUtc"
            (ionInput)="setCreateField('effectiveFromUtc', $event.detail.value)"
            placeholder="p. ej. 2026-10-01T00:00:00Z"
          />
        </ion-item>
        <ion-item>
          <ion-label position="stacked">Vigente hasta (UTC, opcional)</ion-label>
          <ion-input
            [value]="createDraft().effectiveToUtc"
            (ionInput)="setCreateField('effectiveToUtc', $event.detail.value)"
            placeholder="p. ej. 2026-12-31T00:00:00Z"
          />
        </ion-item>

        @if (createValidationError(); as error) {
          <app-error-state [title]="error" />
        }

        <ion-button type="submit" [disabled]="createState().status === 'creating'">
          {{ createState().status === 'creating' ? 'Creando…' : 'Crear regla' }}
        </ion-button>
      </form>

      @switch (createState().status) {
        @case ('created') {
          <section class="admin-card" aria-live="polite">
            <h3>Regla creada</h3>
            <p>
              {{ createdRule()!.useType }} · {{ createdRule()!.componentType }} —
              {{ createdRule()!.amount }} {{ createdRule()!.currency }}
              (desde {{ createdRule()!.effectiveFromUtc }})
            </p>

            @if (supersededRules().length > 0) {
              <p><ion-text color="warning">Reglas reemplazadas (superseded), no editadas:</ion-text></p>
              <ul class="superseded-list">
                @for (rule of supersededRules(); track rule.id) {
                  <li>
                    {{ rule.useType }} · {{ rule.componentType }} — {{ rule.amount }} {{ rule.currency }}
                    (cerrada en {{ createdRule()!.effectiveFromUtc }})
                  </li>
                }
              </ul>
            } @else {
              <p><ion-text color="medium">No se reemplazó ninguna regla existente.</ion-text></p>
            }
          </section>
        }
        @case ('error') {
          <app-error-state [title]="createErrorTitle()" [detail]="createErrorDetail() ?? undefined" />
        }
      }
    </section>
  `
})
export class AdminPricingPage {
  private readonly admin = inject(AdminService);
  private readonly reloadSubject = new Subject<AdminPriceRuleFilters>();

  protected readonly componentTypes = componentTypes;
  protected readonly useTypesOptions = useTypes;
  protected readonly sizes = pageSizes;

  readonly draftBuildingId = signal('');
  readonly draftAmenityId = signal('');
  readonly draftActiveAtUtc = signal('');
  readonly draftPageSize = signal<number>(50);

  readonly filters = signal<AdminPriceRuleFilters | null>(null);
  protected readonly listState = signal<ListState>({ status: 'idle' });
  protected readonly page = computed(() => {
    const state = this.listState();
    return state.status === 'success' || state.status === 'empty' ? state.page : null;
  });
  protected readonly listErrorTitle = computed(() => {
    const state = this.listState();
    return state.status === 'error' ? state.error.title : '';
  });
  protected readonly listErrorDetail = computed(() => {
    const state = this.listState();
    return state.status === 'error' ? (state.error.detail ?? null) : null;
  });
  protected readonly hasNextPage = computed(() => {
    const page = this.page();
    return page ? page.page * page.pageSize < page.totalCount : false;
  });

  readonly createDraft = signal<CreateDraft>(emptyDraft());
  protected readonly createState = signal<CreateState>({ status: 'idle' });
  protected readonly createValidationError = signal<string | null>(null);
  protected readonly createErrorTitle = computed(() => {
    const state = this.createState();
    return state.status === 'error' ? state.error.title : '';
  });
  protected readonly createErrorDetail = computed(() => {
    const state = this.createState();
    return state.status === 'error' ? (state.error.detail ?? null) : null;
  });
  protected readonly createdRule = computed(() => {
    const state = this.createState();
    return state.status === 'created' ? state.result.created : null;
  });
  protected readonly supersededRules = computed(() => {
    const state = this.createState();
    return state.status === 'created' ? state.result.superseded : [];
  });

  constructor() {
    this.reloadSubject
      .pipe(
        switchMap((filters) =>
          this.admin.listPriceRules(filters).pipe(
            map((page): ListState => (page.items.length > 0 ? { status: 'success', page } : { status: 'empty', page })),
            catchError((error: ApiError) => of<ListState>({ status: 'error', error })),
            startWith<ListState>({ status: 'loading' })
          )
        )
      )
      .subscribe((state) => this.listState.set(state));
  }

  applyFilters(event: Event): void {
    event.preventDefault();
    const buildingId = this.draftBuildingId().trim();

    if (!buildingId) {
      this.listState.set({
        status: 'error',
        error: { status: 0, title: 'El Building ID es requerido para buscar reglas.' }
      });
      return;
    }

    const filters: AdminPriceRuleFilters = {
      buildingId,
      amenityId: this.draftAmenityId().trim() || undefined,
      activeAtUtc: this.draftActiveAtUtc().trim() || undefined,
      page: 1,
      pageSize: this.draftPageSize()
    };
    this.filters.set(filters);
    this.reloadSubject.next(filters);
  }

  previousPage(): void {
    this.changePage(Math.max(1, (this.filters()?.page ?? 1) - 1));
  }

  nextPage(): void {
    if (this.hasNextPage()) {
      this.changePage((this.filters()?.page ?? 1) + 1);
    }
  }

  private changePage(page: number): void {
    const current = this.filters();

    if (!current) {
      return;
    }

    const next = { ...current, page };
    this.filters.set(next);
    this.reloadSubject.next(next);
  }

  setCreateField<K extends keyof CreateDraft>(key: K, value: unknown): void {
    this.createDraft.update((current) => ({ ...current, [key]: (value as string) ?? '' }));
  }

  submitCreate(event: Event): void {
    event.preventDefault();

    if (this.createState().status === 'creating') {
      return;
    }

    const draft = this.createDraft();
    const buildingId = draft.buildingId.trim();
    const amenityId = draft.amenityId.trim();
    const currency = draft.currency.trim().toUpperCase();
    const amount = Number(draft.amount);

    if (!buildingId || !amenityId) {
      this.createValidationError.set('Building ID y Amenity ID son requeridos.');
      return;
    }

    if (!/^[A-Z]{3}$/.test(currency)) {
      this.createValidationError.set('La moneda debe tener exactamente 3 letras (ISO 4217), p. ej. ARS.');
      return;
    }

    if (!Number.isFinite(amount) || amount <= 0) {
      this.createValidationError.set('El monto debe ser mayor a 0.');
      return;
    }

    this.createValidationError.set(null);
    this.createState.set({ status: 'creating' });

    const request: CreatePriceRuleRequest = {
      buildingId,
      amenityId,
      componentType: draft.componentType,
      useType: draft.useType,
      currency,
      amount,
      effectiveFromUtc: draft.effectiveFromUtc.trim() || undefined,
      effectiveToUtc: draft.effectiveToUtc.trim() || undefined
    };

    this.admin.createPriceRule(request).subscribe({
      next: (result) => this.createState.set({ status: 'created', result }),
      error: (error: ApiError) => this.createState.set({ status: 'error', error })
    });
  }
}
