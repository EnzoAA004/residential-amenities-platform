import { PaymentMethod, PaymentReservationOutcome, PaymentStatus } from '../../payments/payment.models';
import { ReservationResource } from '../reservation.models';

export interface ResidentReservationPage {
  items: ResidentReservationSummary[];
  page: number;
  pageSize: number;
  totalCount: number;
}

export interface ResidentReservationSummary {
  id: string;
  buildingId: string;
  useType: string;
  status: string;
  startsAtUtc: string;
  endsAtUtc: string;
  createdAtUtc: string;
  expiresAtUtc: string;
  confirmedAtUtc: string | null;
  cancelledAtUtc: string | null;
  expiredAtUtc: string | null;
  cancellationReason: string | null;
  resources: ReservationResource[];
  currency: string;
  totalAmount: number;
}

export interface ResidentReservationPayment {
  paymentId: string;
  reservationId: string;
  method: PaymentMethod;
  status: PaymentStatus;
  amount: number;
  currency: string;
  createdAtUtc: string;
  approvedAtUtc: string | null;
  reservationOutcome: PaymentReservationOutcome;
  requiresManualReview: boolean;
  cashConfirmedAtUtc: string | null;
}
