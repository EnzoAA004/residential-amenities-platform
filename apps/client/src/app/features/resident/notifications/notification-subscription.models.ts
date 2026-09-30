export type NotificationSubscriptionPlatform = 'WebPush' | 'CapacitorNative';

export interface NotificationSubscriptionRegistration {
  platform: NotificationSubscriptionPlatform;
  endpoint: string;
  p256Dh: string;
  auth: string;
  userAgent?: string | null;
}

export interface NotificationSubscription {
  id: string;
  platform: NotificationSubscriptionPlatform;
  endpointHash: string;
  isEnabled: boolean;
  createdAtUtc: string;
  updatedAtUtc: string;
}

export type NotificationPermissionState =
  | 'granted'
  | 'denied'
  | 'prompt'
  | 'unavailable';

export type NotificationRegistrationOutcome =
  | { status: 'registered'; subscription: NotificationSubscription }
  | { status: 'denied' }
  | { status: 'unavailable' };
