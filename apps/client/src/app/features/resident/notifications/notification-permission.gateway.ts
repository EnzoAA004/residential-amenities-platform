import { Injectable } from '@angular/core';

import { NotificationPermissionState } from './notification-subscription.models';

@Injectable({
  providedIn: 'root'
})
export class NotificationPermissionGateway {
  permissionState(): NotificationPermissionState {
    if (!this.isAvailable()) {
      return 'unavailable';
    }

    const permission = window.Notification.permission;

    return permission === 'default' ? 'prompt' : permission;
  }

  async requestPermission(): Promise<NotificationPermissionState> {
    if (!this.isAvailable()) {
      return 'unavailable';
    }

    const permission = await window.Notification.requestPermission();
    return permission === 'default' ? 'prompt' : permission;
  }

  userAgent(): string | null {
    return typeof navigator === 'undefined' ? null : navigator.userAgent;
  }

  private isAvailable(): boolean {
    return (
      typeof window !== 'undefined' &&
      typeof window.Notification !== 'undefined' &&
      window.isSecureContext === true
    );
  }
}
