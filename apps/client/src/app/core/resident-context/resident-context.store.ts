import { Injectable, computed, inject, signal } from '@angular/core';

import { AuthSessionStore } from '../auth/auth-session.store';
import { ResidentMembershipContext } from '../auth/auth.models';

/**
 * A building selection, scoped to the user who made it. Storing the raw
 * `buildingId` alone would let a stale selection made by a previous user
 * resolve again for a different user who happens to share a membership
 * with the same `buildingId` (e.g. after logout + a different user logs
 * in) — `userId` makes that reuse impossible by construction: the selection
 * is only ever read back for the exact user who set it.
 */
interface ResidentBuildingSelection {
  userId: string;
  buildingId: string;
}

/**
 * The resident's active building context, derived from
 * `AuthSessionStore.memberships` — never a second source of truth.
 *
 * - 0 memberships: `activeMembership` is `null`; nothing requests amenities.
 * - 1 membership: it is auto-selected, no selector shown.
 * - N memberships: the caller must call `selectBuilding` with one of the
 *   user's own membership ids; `activeMembership` stays `null` until then.
 *
 * `activeMembership` is fully derived from `memberships()` plus the current
 * user's own selection, so it re-evaluates itself on every session change
 * (login, logout, switching user, `/auth/me` restored after reload) without
 * any explicit reset:
 * - a previously selected building id that is no longer among the current
 *   memberships simply stops resolving to a membership;
 * - a selection made by a *different* user never resolves for the current
 *   one, even if both happen to hold a membership with the same
 *   `buildingId` — selection is identity-scoped, not just id-scoped.
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
  private readonly selectionSignal = signal<ResidentBuildingSelection | null>(null);

  readonly memberships = this.session.memberships;

  readonly activeMembership = computed<ResidentMembershipContext | null>(() => {
    const memberships = this.memberships();

    if (memberships.length === 0) {
      return null;
    }

    if (memberships.length === 1) {
      return memberships[0];
    }

    const currentUserId = this.session.currentUser()?.id ?? null;
    const selection = this.selectionSignal();

    if (!currentUserId || !selection || selection.userId !== currentUserId) {
      return null;
    }

    return memberships.find((membership) => membership.buildingId === selection.buildingId) ?? null;
  });

  readonly activeBuildingId = computed(() => this.activeMembership()?.buildingId ?? null);

  readonly hasNoMembership = computed(() => this.memberships().length === 0);

  readonly requiresSelection = computed(
    () => this.memberships().length > 1 && this.activeMembership() === null
  );

  /**
   * Selects a building by id, but only when that id belongs to one of the
   * currently signed-in user's own memberships. Returns whether the
   * selection was accepted; a rejected selection leaves the previous
   * context untouched and never triggers a request. This is the only path
   * the UI offers to change the active building — there is no way to set
   * an arbitrary id, and the resulting selection is bound to this user's
   * id, so it can never be read back by anyone else.
   */
  selectBuilding(buildingId: string): boolean {
    const currentUserId = this.session.currentUser()?.id;

    if (!currentUserId) {
      return false;
    }

    const belongsToUser = this.memberships().some(
      (membership) => membership.buildingId === buildingId
    );

    if (!belongsToUser) {
      return false;
    }

    this.selectionSignal.set({ userId: currentUserId, buildingId });
    return true;
  }

  clearSelection(): void {
    this.selectionSignal.set(null);
  }
}
