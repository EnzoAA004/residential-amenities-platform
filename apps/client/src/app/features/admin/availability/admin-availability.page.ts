import { Component, computed, inject, signal } from '@angular/core';
import { IonButton, IonInput, IonItem, IonLabel, IonSelect, IonSelectOption, IonSpinner, IonText } from '@ionic/angular';

import { ApiError } from '../../../core/api/api-error';
import { AppErrorStateComponent } from '../../../shared/error-state/app-error-state.component';
import { AdminAvailabilityConfig, AdminDayOfWeek, AvailabilityWindowInput } from '../admin.models';
import { AdminService } from '../admin.service';

type ConfigState =
  | { status: 'idle' }
  | { status: 'loading' }
  | { status: 'success'; config: AdminAvailabilityConfig }
  | { status: 'error'; error: ApiError };

type ReplaceState =
  | { status: 'idle' }
  | { status: 'saving' }
  | { status: 'error'; error: ApiError };

type PeriodCreateState =
  | { status: 'idle' }
  | { status: 'creating' }
  | { status: 'error'; error: ApiError };

interface WindowRow {
  dayOfWeek: AdminDayOfWeek;
  /** `HH:mm` (time input format); converted to `HH:mm:ss` on submit. */
  startTime: string;
  /** `HH:mm` (time input format); converted to `HH:mm:ss` on submit. */
  endTime: string;
}

const dayOptions: { value: AdminDayOfWeek; label: string }[] = [
  { value: 0, label: 'Domingo' },
  { value: 1, label: 'Lunes' },
  { value: 2, label: 'Martes' },
  { value: 3, label: 'Miércoles' },
  { value: 4, label: 'Jueves' },
  { value: 5, label: 'Viernes' },
  { value: 6, label: 'Sábado' }
];

const MAX_WINDOWS = 28;

function toWireTime(input: string): string {
  return input.length === 5 ? `${input}:00` : input;
}

function toInputTime(wire: string): string {
  return wire.length >= 5 ? wire.slice(0, 5) : wire;
}

/**
 * Administrator availability configuration (issue #53): the weekly windows
 * and maintenance/unavailable periods for a single amenity. Replacing the
 * weekly windows is authoritative on the backend (it enforces 1..28 windows,
 * no overnight, no per-day overlap); this UI only blocks what the backend
 * would always reject too. Maintenance periods only shape *future*
 * availability — they never cancel an existing reservation.
 */
@Component({
  selector: 'app-admin-availability-page',
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
      .admin-lookup,
      .period-form {
        display: grid;
        gap: var(--app-space-3);
        grid-template-columns: repeat(auto-fit, minmax(220px, 1fr));
        margin-block-end: var(--app-space-4);
      }

      .window-row {
        display: grid;
        grid-template-columns: repeat(auto-fit, minmax(160px, 1fr)) auto;
        gap: var(--app-space-2);
        align-items: end;
        margin-block-end: var(--app-space-2);
      }

      .admin-list {
        margin: 0;
        padding: 0;
        list-style: none;
        display: grid;
        gap: var(--app-space-2);
      }

      .admin-card {
        border: 1px solid var(--app-border-color);
        border-radius: var(--app-radius-md);
        padding: var(--app-space-3);
        background: var(--app-surface);
      }
    `
  ],
  template: `
    <section aria-labelledby="admin-availability-title">
      <h1 id="admin-availability-title">Disponibilidad de amenities</h1>

      <form class="admin-lookup" (submit)="load($event)">
        <ion-item>
          <ion-label position="stacked">ID del edificio</ion-label>
          <ion-input [value]="draftBuildingId()" (ionInput)="draftBuildingId.set($event.detail.value ?? '')" />
        </ion-item>
        <ion-item>
          <ion-label position="stacked">ID del espacio</ion-label>
          <ion-input [value]="draftAmenityId()" (ionInput)="draftAmenityId.set($event.detail.value ?? '')" />
        </ion-item>
        <ion-button type="submit">Cargar</ion-button>
      </form>

      @switch (configState().status) {
        @case ('idle') {
          <p><ion-text color="medium">Ingresá Building ID y Espacio ID para cargar su configuración.</ion-text></p>
        }
        @case ('loading') {
          <p aria-live="polite">
            <ion-spinner name="dots" /> <ion-text color="medium">Cargando…</ion-text>
          </p>
        }
        @case ('error') {
          <app-error-state [title]="configErrorTitle()" [detail]="configErrorDetail() ?? undefined" />
        }
        @case ('success') {
          <h2>Ventanas semanales</h2>

          @for (row of windowRows(); track $index) {
            <div class="window-row">
              <ion-item>
                <ion-label position="stacked">Día</ion-label>
                <ion-select interface="popover" [value]="row.dayOfWeek" (ionChange)="setRowDay($index, $event.detail.value)">
                  @for (day of days; track day.value) {
                    <ion-select-option [value]="day.value">{{ day.label }}</ion-select-option>
                  }
                </ion-select>
              </ion-item>
              <ion-item>
                <ion-label position="stacked">Desde</ion-label>
                <ion-input type="time" [value]="row.startTime" (ionInput)="setRowStart($index, $event.detail.value)" />
              </ion-item>
              <ion-item>
                <ion-label position="stacked">Hasta</ion-label>
                <ion-input type="time" [value]="row.endTime" (ionInput)="setRowEnd($index, $event.detail.value)" />
              </ion-item>
              <ion-button type="button" fill="outline" color="danger" (click)="removeRow($index)">
                Quitar
              </ion-button>
            </div>
          }

          <p>
            <ion-button type="button" fill="outline" [disabled]="windowRows().length >= maxWindows" (click)="addRow()">
              Agregar ventana
            </ion-button>
          </p>

          @if (replaceValidationError(); as error) {
            <app-error-state [title]="error" />
          }

          <p>
            <ion-button type="button" (click)="submitReplace()" [disabled]="replaceState().status === 'saving'">
              {{ replaceState().status === 'saving' ? 'Guardando…' : 'Reemplazar ventanas semanales' }}
            </ion-button>
          </p>

          @if (replaceState().status === 'error') {
            <app-error-state [title]="replaceErrorTitle()" [detail]="replaceErrorDetail() ?? undefined" />
          }

          <h2>Períodos de mantenimiento / no disponibilidad</h2>
          <p>
            <ion-text color="warning">
              Estos cambios afectan disponibilidad futura; no cancelan reservas existentes.
            </ion-text>
          </p>

          @if (config()!.unavailablePeriods.length === 0) {
            <p><ion-text color="medium">No hay períodos configurados.</ion-text></p>
          } @else {
            <ul class="admin-list" role="list">
              @for (period of config()!.unavailablePeriods; track period.id) {
                <li class="admin-card">
                  <p>{{ period.startsAtUtc }} → {{ period.endsAtUtc }}</p>
                  @if (period.reason) {
                    <p><ion-text color="medium">{{ period.reason }}</ion-text></p>
                  }
                  <ion-button
                    type="button"
                    fill="outline"
                    color="danger"
                    [disabled]="deletingPeriodId() === period.id"
                    (click)="deletePeriod(period.id)"
                  >
                    {{ deletingPeriodId() === period.id ? 'Eliminando…' : 'Eliminar' }}
                  </ion-button>
                </li>
              }
            </ul>
          }

          @if (deleteError(); as error) {
            <app-error-state [title]="error.title" [detail]="error.detail" />
          }

          <form class="period-form" (submit)="submitPeriod($event)">
            <ion-item>
              <ion-label position="stacked">Desde (UTC)</ion-label>
              <ion-input
                type="datetime-local"
                [value]="periodDraft().startsAtUtc"
                (ionInput)="setPeriodField('startsAtUtc', $event.detail.value)"
              />
            </ion-item>
            <ion-item>
              <ion-label position="stacked">Hasta (UTC)</ion-label>
              <ion-input
                type="datetime-local"
                [value]="periodDraft().endsAtUtc"
                (ionInput)="setPeriodField('endsAtUtc', $event.detail.value)"
              />
            </ion-item>
            <ion-item>
              <ion-label position="stacked">Motivo (opcional)</ion-label>
              <ion-input
                [value]="periodDraft().reason"
                (ionInput)="setPeriodField('reason', $event.detail.value)"
              />
            </ion-item>

            @if (periodValidationError(); as error) {
              <app-error-state [title]="error" />
            }

            <ion-button type="submit" [disabled]="periodCreateState().status === 'creating'">
              {{ periodCreateState().status === 'creating' ? 'Creando…' : 'Agregar período' }}
            </ion-button>
          </form>

          @if (periodCreateState().status === 'error') {
            <app-error-state [title]="periodCreateErrorTitle()" [detail]="periodCreateErrorDetail() ?? undefined" />
          }
        }
      }
    </section>
  `
})
export class AdminAvailabilityPage {
  private readonly admin = inject(AdminService);

  protected readonly days = dayOptions;
  protected readonly maxWindows = MAX_WINDOWS;

  readonly draftBuildingId = signal('');
  readonly draftAmenityId = signal('');

  private buildingId = '';
  private amenityId = '';

  protected readonly configState = signal<ConfigState>({ status: 'idle' });
  protected readonly config = computed(() => {
    const state = this.configState();
    return state.status === 'success' ? state.config : null;
  });
  protected readonly configErrorTitle = computed(() => {
    const state = this.configState();
    return state.status === 'error' ? state.error.title : '';
  });
  protected readonly configErrorDetail = computed(() => {
    const state = this.configState();
    return state.status === 'error' ? (state.error.detail ?? null) : null;
  });

  readonly windowRows = signal<WindowRow[]>([]);
  protected readonly replaceValidationError = signal<string | null>(null);
  protected readonly replaceState = signal<ReplaceState>({ status: 'idle' });
  protected readonly replaceErrorTitle = computed(() => {
    const state = this.replaceState();
    return state.status === 'error' ? state.error.title : '';
  });
  protected readonly replaceErrorDetail = computed(() => {
    const state = this.replaceState();
    return state.status === 'error' ? (state.error.detail ?? null) : null;
  });

  protected readonly deletingPeriodId = signal<string | null>(null);
  protected readonly deleteError = signal<ApiError | null>(null);

  readonly periodDraft = signal({ startsAtUtc: '', endsAtUtc: '', reason: '' });
  protected readonly periodValidationError = signal<string | null>(null);
  protected readonly periodCreateState = signal<PeriodCreateState>({ status: 'idle' });
  protected readonly periodCreateErrorTitle = computed(() => {
    const state = this.periodCreateState();
    return state.status === 'error' ? state.error.title : '';
  });
  protected readonly periodCreateErrorDetail = computed(() => {
    const state = this.periodCreateState();
    return state.status === 'error' ? (state.error.detail ?? null) : null;
  });

  load(event: Event): void {
    event.preventDefault();
    const buildingId = this.draftBuildingId().trim();
    const amenityId = this.draftAmenityId().trim();

    if (!buildingId || !amenityId) {
      this.configState.set({
        status: 'error',
        error: { status: 0, title: 'Building ID y Espacio ID son requeridos.' }
      });
      return;
    }

    this.buildingId = buildingId;
    this.amenityId = amenityId;
    this.fetch();
  }

  private fetch(): void {
    this.configState.set({ status: 'loading' });
    this.replaceState.set({ status: 'idle' });
    this.replaceValidationError.set(null);
    this.deleteError.set(null);
    this.periodCreateState.set({ status: 'idle' });
    this.periodValidationError.set(null);

    this.admin.getAvailability(this.amenityId, this.buildingId).subscribe({
      next: (config) => {
        this.configState.set({ status: 'success', config });
        this.windowRows.set(
          config.windows.map((window) => ({
            dayOfWeek: window.dayOfWeek,
            startTime: toInputTime(window.startTime),
            endTime: toInputTime(window.endTime)
          }))
        );
      },
      error: (error: ApiError) => this.configState.set({ status: 'error', error })
    });
  }

  addRow(): void {
    this.windowRows.update((rows) =>
      rows.length >= MAX_WINDOWS ? rows : [...rows, { dayOfWeek: 1, startTime: '', endTime: '' }]
    );
  }

  removeRow(index: number): void {
    this.windowRows.update((rows) => rows.filter((_, i) => i !== index));
  }

  setRowDay(index: number, value: AdminDayOfWeek): void {
    this.windowRows.update((rows) => rows.map((row, i) => (i === index ? { ...row, dayOfWeek: value } : row)));
  }

  setRowStart(index: number, value: string | null | undefined): void {
    this.windowRows.update((rows) =>
      rows.map((row, i) => (i === index ? { ...row, startTime: value ?? '' } : row))
    );
  }

  setRowEnd(index: number, value: string | null | undefined): void {
    this.windowRows.update((rows) => rows.map((row, i) => (i === index ? { ...row, endTime: value ?? '' } : row)));
  }

  submitReplace(): void {
    if (this.replaceState().status === 'saving') {
      return;
    }

    const rows = this.windowRows();

    if (rows.length < 1 || rows.length > MAX_WINDOWS) {
      this.replaceValidationError.set(`Se requieren entre 1 y ${MAX_WINDOWS} ventanas.`);
      return;
    }

    for (const row of rows) {
      if (!row.startTime || !row.endTime) {
        this.replaceValidationError.set('Cada ventana necesita un horario de inicio y fin.');
        return;
      }

      if (row.endTime <= row.startTime) {
        this.replaceValidationError.set('El horario de fin debe ser posterior al de inicio (no se admiten ventanas nocturnas).');
        return;
      }
    }

    this.replaceValidationError.set(null);
    this.replaceState.set({ status: 'saving' });

    const windows: AvailabilityWindowInput[] = rows.map((row) => ({
      dayOfWeek: row.dayOfWeek,
      startTime: toWireTime(row.startTime),
      endTime: toWireTime(row.endTime)
    }));

    this.admin.replaceAvailability(this.amenityId, { buildingId: this.buildingId, windows }).subscribe({
      next: (config) => {
        this.replaceState.set({ status: 'idle' });
        this.configState.set({ status: 'success', config });
        this.windowRows.set(
          config.windows.map((window) => ({
            dayOfWeek: window.dayOfWeek,
            startTime: toInputTime(window.startTime),
            endTime: toInputTime(window.endTime)
          }))
        );
      },
      error: (error: ApiError) => this.replaceState.set({ status: 'error', error })
    });
  }

  setPeriodField(key: 'startsAtUtc' | 'endsAtUtc' | 'reason', value: unknown): void {
    this.periodDraft.update((current) => ({ ...current, [key]: (value as string) ?? '' }));
  }

  submitPeriod(event: Event): void {
    event.preventDefault();

    if (this.periodCreateState().status === 'creating') {
      return;
    }

    const draft = this.periodDraft();

    if (!draft.startsAtUtc || !draft.endsAtUtc) {
      this.periodValidationError.set('Las fechas de inicio y fin son requeridas.');
      return;
    }

    const start = new Date(draft.startsAtUtc);
    const end = new Date(draft.endsAtUtc);

    if (end.getTime() <= start.getTime()) {
      this.periodValidationError.set('La fecha de fin debe ser posterior a la de inicio.');
      return;
    }

    this.periodValidationError.set(null);
    this.periodCreateState.set({ status: 'creating' });

    this.admin
      .createUnavailablePeriod(this.amenityId, {
        buildingId: this.buildingId,
        startsAtUtc: start.toISOString(),
        endsAtUtc: end.toISOString(),
        reason: draft.reason.trim() || null
      })
      .subscribe({
        next: () => {
          this.periodCreateState.set({ status: 'idle' });
          this.periodDraft.set({ startsAtUtc: '', endsAtUtc: '', reason: '' });
          this.fetch();
        },
        error: (error: ApiError) => this.periodCreateState.set({ status: 'error', error })
      });
  }

  deletePeriod(periodId: string): void {
    if (this.deletingPeriodId()) {
      return;
    }

    this.deleteError.set(null);
    this.deletingPeriodId.set(periodId);

    this.admin.deleteUnavailablePeriod(this.amenityId, periodId, this.buildingId).subscribe({
      next: () => {
        this.deletingPeriodId.set(null);
        this.fetch();
      },
      error: (error: ApiError) => {
        this.deletingPeriodId.set(null);
        this.deleteError.set(error);
      }
    });
  }
}
