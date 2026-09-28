export const authRoles = {
  resident: 'Resident',
  administrator: 'Administrator'
} as const;

export type AuthRole = (typeof authRoles)[keyof typeof authRoles];

export interface ResidentMembershipContext {
  buildingId: string;
  unitId: string;
  unit: string;
  building: string;
}

export interface CurrentUser {
  id: string;
  email: string;
  displayName: string;
  roles: AuthRole[];
  memberships: ResidentMembershipContext[];
}

export type AuthState =
  | { status: 'checking' }
  | { status: 'anonymous' }
  | { status: 'authenticated'; user: CurrentUser };

export interface LoginCredentials {
  email: string;
  password: string;
}

