import { Component, computed, inject, signal } from '@angular/core';
import { IonButton, IonInput, IonItem, IonLabel, IonSpinner, IonText } from '@ionic/angular';

import { ApiError } from '../../../core/api/api-error';
import { AdminEventSlot, EventSlotRequest } from '../admin.models';
import { AdminService } from '../admin.service';

type ListState =
  | { status: 'idle' }
  | { status: 'loading' }
  | { status: 'success'; slots: AdminEventSlot[] }
  | { status: 'error'; error: ApiError };

type FormState =
  | { status: 'idle' }
  | { status: 'saving' }
  | { status: 'error'; error: ApiError };

interface FormDraft {
  editingId: string | null;
  name: string;
  /** `HH:mm` (time input format); converted to `HH:mm:ss` on submit. */
  startTime: string;
  /** `HH:mm` (time input format); converted to `HH:mm:ss` on submit. */
  endTime: string;
}

const MAX_NAME_LENGTH = 80;

function emptyDraft(): FormDraft {
  return { editingId: null, name: '', startTime: '', endTime: '' };
}

function toWireTime(input: string): string {
  return input.length === 5 ? `${input}:00` : input;
}

function toInputTime(wire: string): string {
  return wire.length >= 5 ? wire.slice(0, 5) : wire;
}

/**
 * Administrator Event slot configuration (issue #53). There is no delete
 * action: a slot is only ever deactivated (still listed with
 * `isActive: false`) or explicitly reactivated afterwards — never removed.
 */
@Component({
  selector: 'app-admin-event-slots-page',
  standalone: true,
  imports: [IonButton, IonInput, IonItem, IonLabel, IonSpinner, IonText],
  styles: [
    `
      .admin-lookup,
      .slot-form {
        display: grid;
        gap: var(--app-space-3);
        grid-template-columns: repeat(auto-fit, minmax(220px, 1fr));
        margin-block-end: var(--app-space-4);
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
        display: flex;
        flex-wrap: wrap;
        gap: var(--app-space-2);
        align-items: center;
        justify-content: space-between;
      }

      .admin-card.inactive {
        opacity: 0.7;
      }
    `
  ],
  template: `
    <section aria-labelledby="admin-event-slots-title">
      <h1 id="admin-event-slots-title">Turnos de Event</h1>

      <form class="admin-lookup" (submit)="load($event)">
        <ion-item>
          <ion-label position="stacked">Building ID</ion-label>
          <ion-input [value]="draftBuildingId()" (ionInput)="draftBuildingId.set($event.detail.value ?? '')" />
        </ion-item>
        <ion-button type="submit">Cargar</ion-button>
      </form>

      @switch (listState().status) {
        @case ('idle') {
          <p><ion-text color="medium">Ingresá un Building ID para ver sus turnos.</ion-text></p>
        }
        @case ('loading') {
          <p aria-live="polite">
            <ion-spinner name="dots" /> <ion-text color="medium">Cargando…</ion-text>
          </p>
        }
        @case ('error') {
          <p role="alert"><ion-text color="danger">{{ listErrorTitle() }}</ion-text></p>
          @if (listErrorDetail(); as detail) {
            <p role="alert"><ion-text color="danger">{{ detail }}</ion-text></p>
          }
        }
        @case ('success') {
          @if (slots().length === 0) {
            <p><ion-text color="medium">No hay turnos configurados.</ion-text></p>
          } @else {
            <ul class="admin-list" role="list">
              @for (slot of slots(); track slot.id) {
                <li class="admin-card" [class.inactive]="!slot.isActive">
                  <div>
                    <h2>{{ slot.name }}</h2>
                    <p>{{ slot.startTime }} → {{ slot.endTime }}</p>
                    <p>
                      <ion-text [color]="slot.isActive ? 'success' : 'medium'">
                        {{ slot.isActive ? 'Activo' : 'Inactivo' }}
                      </ion-text>
                    </p>
                  </div>
                  <div>
                    <ion-button type="button" fill="outline" (click)="startEdit(slot)">Editar</ion-button>
                    @if (slot.isActive) {
                      <ion-button
                        type="button"
                        fill="outline"
                        color="warning"
                        [disabled]="stateChangeId() === slot.id"
                        (click)="deactivate(slot)"
                      >
                        {{ stateChangeId() === slot.id ? 'Aplicando…' : 'Desactivar' }}
                      </ion-button>
                    } @else {
                      <ion-button
                        type="button"
                        fill="outline"
                        color="success"
                        [disabled]="stateChangeId() === slot.id"
                        (click)="activate(slot)"
                      >
                        {{ stateChangeId() === slot.id ? 'Aplicando…' : 'Reactivar' }}
                      </ion-button>
                    }
                  </div>
                </li>
              }
            </ul>
          }

          @if (stateChangeError(); as error) {
            <p role="alert"><ion-text color="danger">{{ error.title }}</ion-text></p>
            @if (error.detail) {
              <p role="alert"><ion-text color="danger">{{ error.detail }}</ion-text></p>
            }
          }

          <h2>{{ draft().editingId ? 'Editar turno' : 'Crear turno' }}</h2>
          <form class="slot-form" (submit)="submit($event)">
            <ion-item>
              <ion-label position="stacked">Nombre</ion-label>
              <ion-input
                [value]="draft().name"
                (ionInput)="setField('name', $event.detail.value)"
                [maxlength]="maxNameLength"
              />
            </ion-item>
            <ion-item>
              <ion-label position="stacked">Desde</ion-label>
              <ion-input type="time" [value]="draft().startTime" (ionInput)="setField('startTime', $event.detail.value)" />
            </ion-item>
            <ion-item>
              <ion-label position="stacked">Hasta</ion-label>
              <ion-input type="time" [value]="draft().endTime" (ionInput)="setField('endTime', $event.detail.value)" />
            </ion-item>

            @if (validationError(); as error) {
              <p role="alert"><ion-text color="danger">{{ error }}</ion-text></p>
            }

            <div>
              <ion-button type="submit" [disabled]="formState().status === 'saving'">
                {{ formState().status === 'saving' ? 'Guardando…' : draft().editingId ? 'Guardar cambios' : 'Crear turno' }}
              </ion-button>
              @if (draft().editingId) {
                <ion-button type="button" fill="clear" (click)="cancelEdit()">Cancelar</ion-button>
              }
            </div>
          </form>

          @if (formState().status === 'error') {
            <p role="alert"><ion-text color="danger">{{ formErrorTitle() }}</ion-text></p>
            @if (formErrorDetail(); as detail) {
              <p role="alert"><ion-text color="danger">{{ detail }}</ion-text></p>
            }
          }
        }
      }
    </section>
  `
})
export class AdminEventSlotsPage {
  private readonly admin = inject(AdminService);

  protected readonly maxNameLength = MAX_NAME_LENGTH;

  readonly draftBuildingId = signal('');
  private buildingId = '';

  protected readonly listState = signal<ListState>({ status: 'idle' });
  protected readonly slots = computed(() => {
    const state = this.listState();
    return state.status === 'success' ? state.slots : [];
  });
  protected readonly listErrorTitle = computed(() => {
    const state = this.listState();
    return state.status === 'error' ? state.error.title : '';
  });
  protected readonly listErrorDetail = computed(() => {
    const state = this.listState();
    return state.status === 'error' ? (state.error.detail ?? null) : null;
  });

  protected readonly stateChangeId = signal<string | null>(null);
  protected readonly stateChangeError = signal<ApiError | null>(null);

  readonly draft = signal<FormDraft>(emptyDraft());
  protected readonly validationError = signal<string | null>(null);
  protected readonly formState = signal<FormState>({ status: 'idle' });
  protected readonly formErrorTitle = computed(() => {
    const state = this.formState();
    return state.status === 'error' ? state.error.title : '';
  });
  protected readonly formErrorDetail = computed(() => {
    const state = this.formState();
    return state.status === 'error' ? (state.error.detail ?? null) : null;
  });

  load(event: Event): void {
    event.preventDefault();
    const buildingId = this.draftBuildingId().trim();

    if (!buildingId) {
      this.listState.set({ status: 'error', error: { status: 0, title: 'El Building ID es requerido.' } });
      return;
    }

    this.buildingId = buildingId;
    this.fetch();
  }

  private fetch(): void {
    this.listState.set({ status: 'loading' });
    this.stateChangeError.set(null);

    this.admin.listEventSlots(this.buildingId).subscribe({
      next: (slots) => this.listState.set({ status: 'success', slots }),
      error: (error: ApiError) => this.listState.set({ status: 'error', error })
    });
  }

  startEdit(slot: AdminEventSlot): void {
    this.validationError.set(null);
    this.formState.set({ status: 'idle' });
    this.draft.set({
      editingId: slot.id,
      name: slot.name,
      startTime: toInputTime(slot.startTime),
      endTime: toInputTime(slot.endTime)
    });
  }

  cancelEdit(): void {
    this.validationError.set(null);
    this.formState.set({ status: 'idle' });
    this.draft.set(emptyDraft());
  }

  setField(key: 'name' | 'startTime' | 'endTime', value: unknown): void {
    this.draft.update((current) => ({ ...current, [key]: (value as string) ?? '' }));
  }

  submit(event: Event): void {
    event.preventDefault();

    if (this.formState().status === 'saving') {
      return;
    }

    const draft = this.draft();
    const name = draft.name.trim();

    if (!name || name.length > MAX_NAME_LENGTH) {
      this.validationError.set(`El nombre es requerido y debe tener como máximo ${MAX_NAME_LENGTH} caracteres.`);
      return;
    }

    if (!draft.startTime || !draft.endTime) {
      this.validationError.set('Los horarios de inicio y fin son requeridos.');
      return;
    }

    if (draft.endTime <= draft.startTime) {
      this.validationError.set('El horario de fin debe ser posterior al de inicio (no se admiten turnos nocturnos).');
      return;
    }

    if (draft.startTime === '00:00' && draft.endTime >= '23:59') {
      this.validationError.set('El turno no puede abarcar el día completo.');
      return;
    }

    this.validationError.set(null);
    this.formState.set({ status: 'saving' });

    const request: EventSlotRequest = {
      name,
      startTime: toWireTime(draft.startTime),
      endTime: toWireTime(draft.endTime)
    };

    const result$ = draft.editingId
      ? this.admin.updateEventSlot(draft.editingId, request)
      : this.admin.createEventSlot(this.buildingId, request);

    result$.subscribe({
      next: () => {
        this.formState.set({ status: 'idle' });
        this.draft.set(emptyDraft());
        this.fetch();
      },
      error: (error: ApiError) => this.formState.set({ status: 'error', error })
    });
  }

  deactivate(slot: AdminEventSlot): void {
    if (this.stateChangeId()) {
      return;
    }

    this.stateChangeError.set(null);
    this.stateChangeId.set(slot.id);

    this.admin.deactivateEventSlot(slot.id).subscribe({
      next: () => {
        this.stateChangeId.set(null);
        this.fetch();
      },
      error: (error: ApiError) => {
        this.stateChangeId.set(null);
        this.stateChangeError.set(error);
      }
    });
  }

  activate(slot: AdminEventSlot): void {
    if (this.stateChangeId()) {
      return;
    }

    this.stateChangeError.set(null);
    this.stateChangeId.set(slot.id);

    this.admin.activateEventSlot(slot.id).subscribe({
      next: () => {
        this.stateChangeId.set(null);
        this.fetch();
      },
      error: (error: ApiError) => {
        this.stateChangeId.set(null);
        this.stateChangeError.set(error);
      }
    });
  }
}
