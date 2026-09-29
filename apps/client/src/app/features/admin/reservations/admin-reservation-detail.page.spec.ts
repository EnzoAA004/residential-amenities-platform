import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';

import { API_BASE_URL } from '../../../core/config/api-base-url.token';
import { AdminReservationDetail } from '../admin.models';
import { AdminReservationDetailPage } from './admin-reservation-detail.page';

const detail: AdminReservationDetail = {
  reservation: {
    reservationId: 'reservation-1',
    buildingId: 'building-1',
    useType: 'ExclusiveLeisure',
    status: 'Cancelled',
    startsAtUtc: '2026-10-05T10:00:00Z',
    endsAtUtc: '2026-10-05T12:00:00Z',
    createdAtUtc: '2026-10-01T10:00:00Z',
    confirmedAtUtc: null,
    cancelledAtUtc: '2026-10-02T10:00:00Z',
    expiredAtUtc: null,
    expiresAtUtc: '2026-10-01T10:30:00Z',
    cancellationReason: 'Maintenance',
    createdByMembershipId: 'membership-1',
    resources: [{ amenityId: 'amenity-1', isExclusive: true }],
    total: 9000,
    currency: 'ARS'
  },
  priceLines: [{ amenityId: 'amenity-1', componentType: 'Base', currency: 'ARS', amount: 9000 }],
  payments: [
    {
      paymentId: 'payment-1',
      reservationId: 'reservation-1',
      buildingId: 'building-1',
      method: 'Cash',
      status: 'Approved',
      amount: 9000,
      currency: 'ARS',
      createdAtUtc: '2026-10-01T10:05:00Z',
      approvedAtUtc: '2026-10-02T09:00:00Z',
      reservationOutcome: 'ApprovedForCancelledReservation',
      requiresManualReview: true,
      providerStatus: null,
      providerStatusDetail: null,
      cashDeclaredAtUtc: '2026-10-01T10:05:00Z',
      cashConfirmedAtUtc: '2026-10-02T09:00:00Z',
      cashConfirmedByUserId: 'admin-user'
    }
  ],
  requiresFinancialReview: true
};

describe('AdminReservationDetailPage', () => {
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([{ path: 'admin/reservations/:id', component: AdminReservationDetailPage }]),
        { provide: API_BASE_URL, useValue: '/api' }
      ]
    });

    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('loads by route param and renders lifecycle, price lines, payments and review warnings', async () => {
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/admin/reservations/reservation-1', AdminReservationDetailPage);
    httpMock.expectOne('/api/admin/reservations/reservation-1').flush(detail);
    await harness.fixture.whenStable();
    harness.detectChanges();

    const rendered = harness.routeNativeElement!.textContent!;
    expect(rendered).toContain('ExclusiveLeisure · Cancelled');
    expect(rendered).toContain('Maintenance');
    expect(rendered).toContain('Base · 9000 ARS');
    expect(rendered).toContain('Cash · Approved');
    expect(rendered).toContain('ApprovedForCancelledReservation');
    expect(rendered).toContain('Esta reserva requiere revisión financiera');
    expect(rendered).toContain('Este pago requiere revisión administrativa');
    expect(rendered).not.toContain('resolver');
  });

  it('shows backend detail errors without calling mutation endpoints', async () => {
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/admin/reservations/missing', AdminReservationDetailPage);
    httpMock
      .expectOne('/api/admin/reservations/missing')
      .flush({ status: 404, title: 'Reservation not found' }, { status: 404, statusText: 'Not Found' });
    await harness.fixture.whenStable();
    harness.detectChanges();

    expect(harness.routeNativeElement!.textContent!).toContain('Reservation not found');
    httpMock.expectNone((request) => request.method !== 'GET');
  });

  async function loadDetail(harness: RouterTestingHarness, seed: AdminReservationDetail = detail) {
    await harness.navigateByUrl('/admin/reservations/reservation-1', AdminReservationDetailPage);
    httpMock.expectOne('/api/admin/reservations/reservation-1').flush(seed);
    await harness.fixture.whenStable();
    harness.detectChanges();
    return harness.routeDebugElement!.componentInstance as AdminReservationDetailPage;
  }

  describe('cancel', () => {
    it('blocks confirm for empty, whitespace-only and >500 char reasons, allows a valid one', async () => {
      const harness = await RouterTestingHarness.create();
      const page = await loadDetail(harness);
      page.openCancelForm();

      page.cancelForm.controls.reason.setValue('');
      expect(page.isCancelReasonValid()).toBe(false);

      page.cancelForm.controls.reason.setValue('   ');
      expect(page.isCancelReasonValid()).toBe(false);

      page.cancelForm.controls.reason.setValue('a'.repeat(501));
      expect(page.isCancelReasonValid()).toBe(false);

      page.cancelForm.controls.reason.setValue('Maintenance window');
      expect(page.isCancelReasonValid()).toBe(true);

      // Invalid reason: confirmCancel() must not fire a request.
      page.cancelForm.controls.reason.setValue('');
      page.confirmCancel();
      httpMock.expectNone((request) => request.method !== 'GET');
    });

    it('requires the confirmation step to be open before a click can submit', async () => {
      const harness = await RouterTestingHarness.create();
      const page = await loadDetail(harness);

      // The form was never opened (no explicit confirmation step) — a
      // direct confirmCancel() call must be a no-op.
      page.cancelForm.controls.reason.setValue('Maintenance window');
      page.confirmCancel();

      httpMock.expectNone((request) => request.method !== 'GET');
    });

    it('updates the screen from the cancel response only, without an extra GET', async () => {
      const harness = await RouterTestingHarness.create();
      const page = await loadDetail(harness);
      page.openCancelForm();
      page.cancelForm.controls.reason.setValue('Maintenance window');
      page.confirmCancel();

      const request = httpMock.expectOne('/api/admin/reservations/reservation-1/cancel');
      expect(request.request.method).toBe('POST');
      expect(request.request.body).toEqual({ reason: 'Maintenance window' });

      const cancelled: AdminReservationDetail = {
        ...detail,
        reservation: { ...detail.reservation, status: 'Cancelled', cancellationReason: 'Maintenance window' }
      };
      request.flush(cancelled);
      await harness.fixture.whenStable();
      harness.detectChanges();

      expect(page.detail()).toEqual(cancelled);
      expect(page.cancelState()).toEqual({ status: 'idle' });
      httpMock.expectNone('/api/admin/reservations/reservation-1');
    });

    it('renders no error for an already-cancelled reservation (idempotent 200)', async () => {
      const harness = await RouterTestingHarness.create();
      const page = await loadDetail(harness);
      page.openCancelForm();
      page.cancelForm.controls.reason.setValue('Already cancelled, confirming again');
      page.confirmCancel();

      httpMock.expectOne('/api/admin/reservations/reservation-1/cancel').flush(detail);
      await harness.fixture.whenStable();
      harness.detectChanges();

      expect(page.cancelState()).toEqual({ status: 'idle' });
      expect(harness.routeNativeElement!.textContent!).not.toContain('Cargando reserva');
      expect(page.state().status).toBe('success');
    });

    it('shows the real 409 detail when the reservation is not cancellable', async () => {
      const harness = await RouterTestingHarness.create();
      const page = await loadDetail(harness);
      page.openCancelForm();
      page.cancelForm.controls.reason.setValue('Trying to cancel');
      page.confirmCancel();

      httpMock
        .expectOne('/api/admin/reservations/reservation-1/cancel')
        .flush(
          { status: 409, title: 'Unable to cancel this reservation.', detail: 'The reservation already expired.' },
          { status: 409, statusText: 'Conflict' }
        );
      await harness.fixture.whenStable();
      harness.detectChanges();

      expect(page.cancelState()).toEqual({
        status: 'error',
        error: {
          status: 409,
          title: 'Unable to cancel this reservation.',
          detail: 'The reservation already expired.'
        }
      });
      expect(harness.routeNativeElement!.textContent!).toContain('The reservation already expired.');
    });

    it('shows the real 400 detail for an invalid cancel request', async () => {
      const harness = await RouterTestingHarness.create();
      const page = await loadDetail(harness);
      page.openCancelForm();
      page.cancelForm.controls.reason.setValue('Trying to cancel');
      page.confirmCancel();

      httpMock
        .expectOne('/api/admin/reservations/reservation-1/cancel')
        .flush(
          { status: 400, title: 'This request is invalid.', detail: 'Reason is required.' },
          { status: 400, statusText: 'Bad Request' }
        );
      await harness.fixture.whenStable();
      harness.detectChanges();

      expect(harness.routeNativeElement!.textContent!).toContain('Reason is required.');
    });

    it('blocks a second submit while cancelling (no double POST)', async () => {
      const harness = await RouterTestingHarness.create();
      const page = await loadDetail(harness);
      page.openCancelForm();
      page.cancelForm.controls.reason.setValue('Maintenance window');
      page.confirmCancel();

      expect(page.cancelState()).toEqual({ status: 'submitting' });

      // A second explicit attempt while still submitting must not fire
      // another POST.
      page.confirmCancel();

      const request = httpMock.expectOne('/api/admin/reservations/reservation-1/cancel');
      request.flush(detail);
      await harness.fixture.whenStable();

      httpMock.verify();
    });

    it('never touches an Approved payment on cancel and preserves requiresFinancialReview from the response', async () => {
      const harness = await RouterTestingHarness.create();
      const page = await loadDetail(harness);
      page.openCancelForm();
      page.cancelForm.controls.reason.setValue('Maintenance window');
      page.confirmCancel();

      const cancelled: AdminReservationDetail = {
        ...detail,
        reservation: { ...detail.reservation, status: 'Cancelled', cancellationReason: 'Maintenance window' },
        payments: [{ ...detail.payments[0], status: 'Approved' }],
        requiresFinancialReview: true
      };
      httpMock.expectOne('/api/admin/reservations/reservation-1/cancel').flush(cancelled);
      await harness.fixture.whenStable();
      harness.detectChanges();

      expect(page.detail()!.payments[0].status).toBe('Approved');
      expect(page.detail()!.requiresFinancialReview).toBe(true);
      const rendered = harness.routeNativeElement!.textContent!;
      expect(rendered).not.toContain('reembolso iniciado');
      expect(rendered).not.toContain('pago cancelado');
    });
  });

  describe('reschedule', () => {
    function fillValidRescheduleForm(page: AdminReservationDetailPage): void {
      page.rescheduleForm.setValue({
        startDate: '2026-11-01',
        startTime: '10:00',
        endDate: '2026-11-01',
        endTime: '12:00',
        reason: 'Requested by resident'
      });
    }

    it('requires the confirmation step to be open before a click can submit', async () => {
      const harness = await RouterTestingHarness.create();
      const page = await loadDetail(harness);
      fillValidRescheduleForm(page);

      page.confirmReschedule();

      httpMock.expectNone((request) => request.method !== 'GET');
    });

    it('validates reason and required UTC fields, and blocks an invalid range', async () => {
      const harness = await RouterTestingHarness.create();
      const page = await loadDetail(harness);
      page.openRescheduleForm();

      page.rescheduleForm.setValue({
        startDate: '',
        startTime: '',
        endDate: '',
        endTime: '',
        reason: ''
      });
      expect(page.isRescheduleFormValid()).toBe(false);

      fillValidRescheduleForm(page);
      page.rescheduleForm.controls.reason.setValue('a'.repeat(501));
      expect(page.isRescheduleFormValid()).toBe(false);

      fillValidRescheduleForm(page);
      page.rescheduleForm.controls.endTime.setValue('09:00'); // ends before it starts
      expect(page.isRescheduleFormValid()).toBe(false);

      fillValidRescheduleForm(page);
      expect(page.isRescheduleFormValid()).toBe(true);
    });

    it('constructs startsAtUtc/endsAtUtc without a local Date parse and sends exactly the 3 request fields', async () => {
      const harness = await RouterTestingHarness.create();
      const page = await loadDetail(harness);
      page.openRescheduleForm();
      fillValidRescheduleForm(page);
      page.confirmReschedule();

      const request = httpMock.expectOne('/api/admin/reservations/reservation-1/reschedule');
      expect(request.request.method).toBe('POST');
      expect(request.request.body).toEqual({
        startsAtUtc: '2026-11-01T10:00:00.000Z',
        endsAtUtc: '2026-11-01T12:00:00.000Z',
        reason: 'Requested by resident'
      });
      request.flush(detail);
    });

    it('updates the screen from the reschedule response only, without an extra GET', async () => {
      const harness = await RouterTestingHarness.create();
      const page = await loadDetail(harness);
      page.openRescheduleForm();
      fillValidRescheduleForm(page);
      page.confirmReschedule();

      const rescheduled: AdminReservationDetail = {
        ...detail,
        reservation: {
          ...detail.reservation,
          startsAtUtc: '2026-11-01T10:00:00.000Z',
          endsAtUtc: '2026-11-01T12:00:00.000Z'
        }
      };
      httpMock.expectOne('/api/admin/reservations/reservation-1/reschedule').flush(rescheduled);
      await harness.fixture.whenStable();
      harness.detectChanges();

      expect(page.detail()).toEqual(rescheduled);
      httpMock.expectNone('/api/admin/reservations/reservation-1');
    });

    it('shows the real 400 detail (invalid range/reason)', async () => {
      const harness = await RouterTestingHarness.create();
      const page = await loadDetail(harness);
      page.openRescheduleForm();
      fillValidRescheduleForm(page);
      page.confirmReschedule();

      httpMock
        .expectOne('/api/admin/reservations/reservation-1/reschedule')
        .flush(
          { status: 400, title: 'This request is invalid.', detail: 'endsAtUtc must be after startsAtUtc.' },
          { status: 400, statusText: 'Bad Request' }
        );
      await harness.fixture.whenStable();
      harness.detectChanges();

      expect(harness.routeNativeElement!.textContent!).toContain('endsAtUtc must be after startsAtUtc.');
    });

    it('shows the real 409 detail for a non-reschedulable state', async () => {
      const harness = await RouterTestingHarness.create();
      const page = await loadDetail(harness);
      page.openRescheduleForm();
      fillValidRescheduleForm(page);
      page.confirmReschedule();

      httpMock
        .expectOne('/api/admin/reservations/reservation-1/reschedule')
        .flush(
          { status: 409, title: 'Unable to reschedule this reservation.', detail: 'The reservation is Cancelled.' },
          { status: 409, statusText: 'Conflict' }
        );
      await harness.fixture.whenStable();
      harness.detectChanges();

      expect(harness.routeNativeElement!.textContent!).toContain('The reservation is Cancelled.');
    });

    it('shows the real 409 detail for a booking conflict, distinguished only by detail text', async () => {
      const harness = await RouterTestingHarness.create();
      const page = await loadDetail(harness);
      page.openRescheduleForm();
      fillValidRescheduleForm(page);
      page.confirmReschedule();

      httpMock
        .expectOne('/api/admin/reservations/reservation-1/reschedule')
        .flush(
          { status: 409, title: 'Unable to reschedule this reservation.', detail: 'This time range is not available.' },
          { status: 409, statusText: 'Conflict' }
        );
      await harness.fixture.whenStable();
      harness.detectChanges();

      expect(harness.routeNativeElement!.textContent!).toContain('This time range is not available.');
    });

    it('shows the real 422 detail for outside availability', async () => {
      const harness = await RouterTestingHarness.create();
      const page = await loadDetail(harness);
      page.openRescheduleForm();
      fillValidRescheduleForm(page);
      page.confirmReschedule();

      httpMock
        .expectOne('/api/admin/reservations/reservation-1/reschedule')
        .flush(
          { status: 422, title: 'Unable to reschedule this reservation.', detail: 'Outside availability.' },
          { status: 422, statusText: 'Unprocessable Entity' }
        );
      await harness.fixture.whenStable();
      harness.detectChanges();

      expect(harness.routeNativeElement!.textContent!).toContain('Outside availability.');
    });

    it('shows the real 422 detail for an invalid Event slot without inventing a suggested time', async () => {
      const harness = await RouterTestingHarness.create();
      const page = await loadDetail(harness);
      page.openRescheduleForm();
      fillValidRescheduleForm(page);
      page.confirmReschedule();

      httpMock
        .expectOne('/api/admin/reservations/reservation-1/reschedule')
        .flush(
          { status: 422, title: 'Unable to reschedule this reservation.', detail: 'Invalid Event slot.' },
          { status: 422, statusText: 'Unprocessable Entity' }
        );
      await harness.fixture.whenStable();
      harness.detectChanges();

      const rendered = harness.routeNativeElement!.textContent!;
      expect(rendered).toContain('Invalid Event slot.');
      expect(rendered).not.toContain('suggested time');
      expect(rendered).not.toContain('horario sugerido');
    });

    it('shows the real 404 detail for a missing reservation', async () => {
      const harness = await RouterTestingHarness.create();
      const page = await loadDetail(harness);
      page.openRescheduleForm();
      fillValidRescheduleForm(page);
      page.confirmReschedule();

      httpMock
        .expectOne('/api/admin/reservations/reservation-1/reschedule')
        .flush({ status: 404, title: 'Reservation not found' }, { status: 404, statusText: 'Not Found' });
      await harness.fixture.whenStable();
      harness.detectChanges();

      expect(harness.routeNativeElement!.textContent!).toContain('Reservation not found');
    });

    it('blocks a second submit while rescheduling (no double POST)', async () => {
      const harness = await RouterTestingHarness.create();
      const page = await loadDetail(harness);
      page.openRescheduleForm();
      fillValidRescheduleForm(page);
      page.confirmReschedule();

      expect(page.rescheduleState()).toEqual({ status: 'submitting' });

      page.confirmReschedule();

      const request = httpMock.expectOne('/api/admin/reservations/reservation-1/reschedule');
      request.flush(detail);
      await harness.fixture.whenStable();

      httpMock.verify();
    });

    it('never allows editing buildingId, resources, price/currency or expiresAtUtc', async () => {
      const harness = await RouterTestingHarness.create();
      const page = await loadDetail(harness);
      page.openRescheduleForm();
      fillValidRescheduleForm(page);
      page.confirmReschedule();

      const request = httpMock.expectOne('/api/admin/reservations/reservation-1/reschedule');
      expect(Object.keys(request.request.body as object).sort()).toEqual(
        ['endsAtUtc', 'reason', 'startsAtUtc'].sort()
      );
      request.flush(detail);
    });
  });
});
