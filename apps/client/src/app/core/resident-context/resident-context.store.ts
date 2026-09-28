import { Injectable, computed, inject, signal } from '@angular/core';

import { AuthSessionStore } from '../auth/auth-session.store';
import { ResidentMembershipContext } from '../auth/auth.models';

/**
 * The resident's active building context, derived from
 * `AuthSessionStore.memberships` — never a second source of truth.
 *
 * - 0 memberships: `activeMembership` is `null`; nothing requests amenities.
 * - 1 membership: it is auto-selected, no selector shown.
 * - N memberships: the caller must call `selectBuilding` with one of the
 *   user's own membership ids; `activeMembership` stays `null` until then.
 *
 * `activeMembership` is fully derived from `memberships()` plus the raw
 * selection, so it re-evaluates itself on every session change (login,
 * logout, switching user, `/auth/me` restored after reload) without any
 * explicit reset: a previously selected building id that is no longer among
 * the current memberships simply stops resolving to a membership.
 *
 * Selection is in-memory only (no localStorage/sessionStorage/IndexedDB) —
 * a reload with a single membership reconstructs itself; a reload with
 * several asks the resident to choose again.
 */
@Injectable({
  providedIn: 'root'
})
export class ResidentContextStore {
  private readonly session = inject(AuthSessionStore);
  private readonly selectedBuildingIdSignal = signal<string | null>(null);

  readonly memberships = this.session.memberships;

  readonly activeMembership = computed<ResidentMembershipContext | null>(() => {
    const memberships = this.memberships();

    if (memberships.length === 0) {
      return null;
    }

    if (memberships.length === 1) {
      return memberships[0];
    }

    const selectedBuildingId = this.selectedBuildingIdSignal();
    return memberships.find((membership) => membership.buildingId === selectedBuildingId) ?? null;
  });

  readonly activeBuildingId = computed(() => this.activeMembership()?.buildingId ?? null);

  readonly hasNoMembership = computed(() => this.memberships().length === 0);

  readonly requiresSelection = computed(
    () => this.memberships().length > 1 && this.activeMembership() === null
  );

  /**
   * Selects a building by id, but only when that id belongs to one of the
   * signed-in user's own memberships. Returns whether the selection was
   * accepted; a rejected selection leaves the previous context untouched
   * and never triggers a request. This is the only path the UI offers to
   * change the active building — there is no way to set an arbitrary id.
   */
  selectBuilding(buildingId: string): boolean {
    const belongsToUser = this.memberships().some(
      (membership) => membership.buildingId === buildingId
    );

    if (!belongsToUser) {
      return false;
    }

    this.selectedBuildingIdSignal.set(buildingId);
    return true;
  }

  clearSelection(): void {
    this.selectedBuildingIdSignal.set(null);
  }
}
