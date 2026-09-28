import { TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';

import { CurrentUser } from '../auth/auth.models';
import { AuthSessionStore } from '../auth/auth-session.store';
import { ResidentContextStore } from './resident-context.store';

function userWith(memberships: CurrentUser['memberships'], id = 'user-1'): CurrentUser {
  return {
    id,
    email: `${id}@example.test`,
    displayName: `Resident ${id}`,
    roles: ['Resident'],
    memberships
  };
}

const buildingA = { buildingId: 'building-a', unitId: 'unit-a', unit: '1A', building: 'Building A' };
const buildingB = { buildingId: 'building-b', unitId: 'unit-b', unit: '2B', building: 'Building B' };

describe('ResidentContextStore', () => {
  function setup() {
    TestBed.configureTestingModule({ providers: [AuthSessionStore, ResidentContextStore] });
    return {
      session: TestBed.inject(AuthSessionStore),
      store: TestBed.inject(ResidentContextStore)
    };
  }

  it('has no active membership and no building when the resident has none', () => {
    const { session, store } = setup();
    session.setAuthenticated(userWith([]));

    expect(store.activeMembership()).toBeNull();
    expect(store.activeBuildingId()).toBeNull();
    expect(store.hasNoMembership()).toBe(true);
    expect(store.requiresSelection()).toBe(false);
  });

  it('auto-selects the single membership without requiring a selector', () => {
    const { session, store } = setup();
    session.setAuthenticated(userWith([buildingA]));

    expect(store.activeMembership()).toEqual(buildingA);
    expect(store.activeBuildingId()).toBe('building-a');
    expect(store.requiresSelection()).toBe(false);
  });

  it('does not arbitrarily pick a membership when there are several', () => {
    const { session, store } = setup();
    session.setAuthenticated(userWith([buildingA, buildingB]));

    expect(store.activeMembership()).toBeNull();
    expect(store.requiresSelection()).toBe(true);
  });

  it('selects a valid membership among several', () => {
    const { session, store } = setup();
    session.setAuthenticated(userWith([buildingA, buildingB]));

    const accepted = store.selectBuilding('building-b');

    expect(accepted).toBe(true);
    expect(store.activeMembership()).toEqual(buildingB);
    expect(store.requiresSelection()).toBe(false);
  });

  it('rejects a building id that does not belong to the user', () => {
    const { session, store } = setup();
    session.setAuthenticated(userWith([buildingA, buildingB]));

    const accepted = store.selectBuilding('someone-elses-building');

    expect(accepted).toBe(false);
    expect(store.activeMembership()).toBeNull();
  });

  it('clears the active membership on logout', () => {
    const { session, store } = setup();
    session.setAuthenticated(userWith([buildingA]));
    expect(store.activeMembership()).toEqual(buildingA);

    session.setAnonymous();

    expect(store.activeMembership()).toBeNull();
    expect(store.hasNoMembership()).toBe(true);
  });

  it('revalidates the selection when the user changes: keeps it if still exactly one valid match', () => {
    const { session, store } = setup();
    session.setAuthenticated(userWith([buildingA, buildingB]));
    store.selectBuilding('building-b');
    expect(store.activeMembership()).toEqual(buildingB);

    // A different user logs in with a single, different membership.
    const otherBuilding = { buildingId: 'building-c', unitId: 'unit-c', unit: '3C', building: 'Building C' };
    session.setAuthenticated(userWith([otherBuilding]));

    expect(store.activeMembership()).toEqual(otherBuilding);
  });

  it('clears the selection when the user changes and the previous selection no longer belongs to them', () => {
    const { session, store } = setup();
    session.setAuthenticated(userWith([buildingA, buildingB]));
    store.selectBuilding('building-b');
    expect(store.activeMembership()).toEqual(buildingB);

    const otherBuilding = { buildingId: 'building-c', unitId: 'unit-c', unit: '3C', building: 'Building C' };
    const otherBuildingD = { buildingId: 'building-d', unitId: 'unit-d', unit: '4D', building: 'Building D' };
    session.setAuthenticated(userWith([otherBuilding, otherBuildingD]));

    expect(store.activeMembership()).toBeNull();
    expect(store.requiresSelection()).toBe(true);
  });

  it(
    'never reactivates a previous user\'s selection for a different user, even when they share a buildingId',
    () => {
      const { session, store } = setup();

      // User A has two memberships and explicitly selects building-b.
      session.setAuthenticated(userWith([buildingA, buildingB], 'user-a'));
      const accepted = store.selectBuilding('building-b');
      expect(accepted).toBe(true);
      expect(store.activeMembership()).toEqual(buildingB);

      session.setAnonymous();
      expect(store.activeMembership()).toBeNull();

      // User C logs in with a membership set that happens to include the
      // same building-b — but never selected it themselves.
      const buildingC = { buildingId: 'building-c', unitId: 'unit-c', unit: '3C', building: 'Building C' };
      session.setAuthenticated(userWith([buildingB, buildingC], 'user-c'));

      expect(store.activeMembership()).toBeNull();
      expect(store.requiresSelection()).toBe(true);
    }
  );

  it('keeps a selection for the same user across an /auth/me refresh', () => {
    const { session, store } = setup();
    session.setAuthenticated(userWith([buildingA, buildingB], 'user-a'));
    store.selectBuilding('building-b');

    // Same user, memberships array replaced (e.g. /auth/me re-fetched).
    session.setAuthenticated(userWith([buildingA, buildingB], 'user-a'));

    expect(store.activeMembership()).toEqual(buildingB);
  });

  it('does not carry a selection over to a different user even with the identical buildingId set', () => {
    const { session, store } = setup();
    session.setAuthenticated(userWith([buildingA, buildingB], 'user-a'));
    store.selectBuilding('building-b');

    session.setAuthenticated(userWith([buildingA, buildingB], 'user-b'));

    expect(store.activeMembership()).toBeNull();
    expect(store.requiresSelection()).toBe(true);
  });

  it('still auto-selects a new user\'s single membership after a different user had made a selection', () => {
    const { session, store } = setup();
    session.setAuthenticated(userWith([buildingA, buildingB], 'user-a'));
    store.selectBuilding('building-b');
    session.setAnonymous();

    session.setAuthenticated(userWith([buildingA], 'user-c'));

    expect(store.activeMembership()).toEqual(buildingA);
  });

  it('rejects selectBuilding while anonymous (no current user to scope the selection to)', () => {
    const { store } = setup();

    expect(store.selectBuilding('building-a')).toBe(false);
    expect(store.activeMembership()).toBeNull();
  });
});
