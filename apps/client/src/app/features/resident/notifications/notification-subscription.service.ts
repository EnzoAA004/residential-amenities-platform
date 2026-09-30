import { Injectable, inject } from '@angular/core';
import { Observable, from, map, of, switchMap } from 'rxjs';

import { ApiClient } from '../../../core/api/api-client.service';
import { apiPaths } from '../../../core/api/api-paths';
import { NotificationPermissionGateway } from './notification-permission.gateway';
import {
  NotificationRegistrationOutcome,
  NotificationSubscription,
  NotificationSubscriptionRegistration
} from './notification-subscription.models';

@Injectable({
  providedIn: 'root'
})
export class NotificationSubscriptionService {
  private readonly api = inject(ApiClient);
  private readonly permissions = inject(NotificationPermissionGateway);

  list(): Observable<NotificationSubscription[]> {
    return this.api.get<NotificationSubscription[]>(apiPaths.notifications.subscriptions);
  }

  registerWebPush(
    registration: Omit<NotificationSubscriptionRegistration, 'platform' | 'userAgent'>
  ): Observable<NotificationRegistrationOutcome> {
    const state = this.permissions.permissionState();

    if (state === 'unavailable') {
      return of({ status: 'unavailable' });
    }

    if (state === 'denied') {
      return of({ status: 'denied' });
    }

    const permission$ =
      state === 'prompt' ? from(this.permissions.requestPermission()) : of(state);

    return permission$.pipe(
      switchMap((permission) => {
        if (permission !== 'granted') {
          return of<NotificationRegistrationOutcome>(
            permission === 'denied' ? { status: 'denied' } : { status: 'unavailable' }
          );
        }

        return this.api
          .post<NotificationSubscription, NotificationSubscriptionRegistration>(
            apiPaths.notifications.subscriptions,
            {
              platform: 'WebPush',
              ...registration,
              userAgent: this.permissions.userAgent()
            }
          )
          .pipe(
            map((subscription): NotificationRegistrationOutcome => ({
              status: 'registered',
              subscription
            }))
          );
      })
    );
  }

  unregister(subscriptionId: string): Observable<void> {
    return this.api.delete<void>(apiPaths.notifications.subscriptionById(subscriptionId));
  }
}
