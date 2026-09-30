import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';
import { beforeEach, describe, expect, it } from 'vitest';

import { API_BASE_URL } from '../../../core/config/api-base-url.token';
import { NotificationPermissionGateway } from './notification-permission.gateway';
import { NotificationPermissionState } from './notification-subscription.models';
import { NotificationSubscriptionService } from './notification-subscription.service';

class FakeNotificationPermissionGateway {
  state: NotificationPermissionState = 'granted';
  requestedState: NotificationPermissionState = 'granted';
  requestCount = 0;

  permissionState(): NotificationPermissionState {
    return this.state;
  }

  async requestPermission(): Promise<NotificationPermissionState> {
    this.requestCount += 1;
    return this.requestedState;
  }

  userAgent(): string {
    return 'Test UA';
  }
}

describe('NotificationSubscriptionService', () => {
  let service: NotificationSubscriptionService;
  let httpMock: HttpTestingController;
  let permissions: FakeNotificationPermissionGateway;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: API_BASE_URL, useValue: '/api' },
        {
          provide: NotificationPermissionGateway,
          useClass: FakeNotificationPermissionGateway
        }
      ]
    });

    service = TestBed.inject(NotificationSubscriptionService);
    httpMock = TestBed.inject(HttpTestingController);
    permissions = TestBed.inject(
      NotificationPermissionGateway
    ) as unknown as FakeNotificationPermissionGateway;
  });

  it('registers a web push subscription only after permission is granted', async () => {
    permissions.state = 'prompt';
    permissions.requestedState = 'granted';

    const outcomePromise = firstValueFrom(
      service.registerWebPush({
        endpoint: 'https://push.example.test/subscriptions/abc',
        p256Dh: 'p256dh-key',
        auth: 'auth-secret'
      })
    );

    const request = httpMock.expectOne('/api/notification-subscriptions');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({
      platform: 'WebPush',
      endpoint: 'https://push.example.test/subscriptions/abc',
      p256Dh: 'p256dh-key',
      auth: 'auth-secret',
      userAgent: 'Test UA'
    });

    request.flush({
      id: 'subscription-1',
      platform: 'WebPush',
      endpointHash: 'hash',
      isEnabled: true,
      createdAtUtc: '2026-09-30T00:00:00Z',
      updatedAtUtc: '2026-09-30T00:00:00Z'
    });

    await expect(outcomePromise).resolves.toEqual({
      status: 'registered',
      subscription: {
        id: 'subscription-1',
        platform: 'WebPush',
        endpointHash: 'hash',
        isEnabled: true,
        createdAtUtc: '2026-09-30T00:00:00Z',
        updatedAtUtc: '2026-09-30T00:00:00Z'
      }
    });
    expect(permissions.requestCount).toBe(1);
  });

  it('does not send the token when permission is denied', async () => {
    permissions.state = 'denied';

    const outcome = await firstValueFrom(
      service.registerWebPush({
        endpoint: 'https://push.example.test/subscriptions/abc',
        p256Dh: 'p256dh-key',
        auth: 'auth-secret'
      })
    );

    expect(outcome).toEqual({ status: 'denied' });
    httpMock.expectNone('/api/notification-subscriptions');
  });

  it('does not send the token when notifications are unavailable', async () => {
    permissions.state = 'unavailable';

    const outcome = await firstValueFrom(
      service.registerWebPush({
        endpoint: 'https://push.example.test/subscriptions/abc',
        p256Dh: 'p256dh-key',
        auth: 'auth-secret'
      })
    );

    expect(outcome).toEqual({ status: 'unavailable' });
    httpMock.expectNone('/api/notification-subscriptions');
  });

  it('lists and unregisters subscriptions using safe backend response data', async () => {
    const listPromise = firstValueFrom(service.list());

    const list = httpMock.expectOne('/api/notification-subscriptions');
    expect(list.request.method).toBe('GET');
    list.flush([
      {
        id: 'subscription-1',
        platform: 'WebPush',
        endpointHash: 'hash',
        isEnabled: true,
        createdAtUtc: '2026-09-30T00:00:00Z',
        updatedAtUtc: '2026-09-30T00:00:00Z'
      }
    ]);

    await expect(listPromise).resolves.toHaveLength(1);

    const unregisterPromise = firstValueFrom(service.unregister('subscription-1'));
    const unregister = httpMock.expectOne('/api/notification-subscriptions/subscription-1');
    expect(unregister.request.method).toBe('DELETE');
    unregister.flush(null);

    await expect(unregisterPromise).resolves.toBeNull();
  });
});
