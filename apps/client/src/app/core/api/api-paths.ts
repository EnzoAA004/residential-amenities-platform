/**
 * The single source of truth for backend API paths.
 *
 * Every path here is **relative** (no leading `/api`): `ApiClient` already
 * resolves calls against `API_BASE_URL` (same-origin `/api`), so a path of
 * `/auth/login` becomes a request to `/api/auth/login`. Never prefix an
 * entry with `/api` — that would produce a duplicated `/api/api/...` URL.
 *
 * This only lists endpoints that exist today in `apps/api` (verified against
 * `Modules/*​/​*Endpoints.cs`). A feature that needs an endpoint that is not
 * here does not have that endpoint yet — add it only once the corresponding
 * backend route exists.
 */
export const apiPaths = {
  auth: {
    login: '/auth/login',
    refresh: '/auth/refresh',
    logout: '/auth/logout',
    me: '/auth/me',
    checkResident: '/auth/check/resident',
    checkAdmin: '/auth/check/admin'
  },

  amenities: {
    listForBuilding: (buildingId: string) => `/buildings/${buildingId}/amenities`,
    availability: (amenityId: string) => `/amenities/${amenityId}/availability`
  },

  reservations: {
    create: '/reservations',
    list: '/reservations',
    byId: (id: string) => `/reservations/${id}`,
    payments: (id: string) => `/reservations/${id}/payments`,
    // Resident-facing Event slot discovery (issue #62) — distinct from
    // admin.eventSlots below, which is Administrator-only.
    eventSlotsForBuilding: (buildingId: string) => `/buildings/${buildingId}/event-slots`
  },

  pricing: {
    quote: '/pricing/quote'
  },

  payments: {
    initiateMercadoPago: (reservationId: string) =>
      `/reservations/${reservationId}/payments/mercadopago`,
    declareCash: (reservationId: string) => `/reservations/${reservationId}/payments/cash`,
    confirmCash: (paymentId: string) => `/payments/${paymentId}/cash/confirm`,
    byId: (paymentId: string) => `/payments/${paymentId}`
  },

  admin: {
    reservations: {
      list: '/admin/reservations',
      byId: (id: string) => `/admin/reservations/${id}`,
      cancel: (id: string) => `/admin/reservations/${id}/cancel`,
      reschedule: (id: string) => `/admin/reservations/${id}/reschedule`
    },

    payments: {
      list: '/admin/payments'
    },

    pricing: {
      rules: '/admin/pricing/rules'
    },

    amenities: {
      availability: (amenityId: string) => `/admin/amenities/${amenityId}/availability`,
      unavailablePeriods: (amenityId: string) =>
        `/admin/amenities/${amenityId}/unavailable-periods`,
      unavailablePeriodById: (amenityId: string, periodId: string) =>
        `/admin/amenities/${amenityId}/unavailable-periods/${periodId}`
    },

    eventSlots: {
      listForBuilding: (buildingId: string) => `/admin/buildings/${buildingId}/event-slots`,
      byId: (id: string) => `/admin/event-slots/${id}`,
      deactivate: (id: string) => `/admin/event-slots/${id}/deactivate`,
      activate: (id: string) => `/admin/event-slots/${id}/activate`
    },

    // Lives under admin because the backend itself groups it at
    // /api/admin/audit (Modules/Audit/AuditEndpoints.cs) — the audit trail
    // is an administrative read model, not its own top-level API area.
    audit: {
      list: '/admin/audit'
    }
  }
} as const;
