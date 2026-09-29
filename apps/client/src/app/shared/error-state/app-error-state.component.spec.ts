import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { describe, expect, it, vi } from 'vitest';

import { AppErrorStateComponent } from './app-error-state.component';

@Component({
  standalone: true,
  imports: [AppErrorStateComponent],
  template: `
    <app-error-state
      [title]="title"
      [detail]="detail"
      [showRetry]="showRetry"
      (retry)="onRetry()"
    />
  `
})
class HostComponent {
  title = 'No pudimos cargar los datos.';
  detail?: string;
  showRetry = false;
  retried = 0;

  onRetry(): void {
    this.retried++;
  }
}

describe('AppErrorStateComponent', () => {
  it('renders the title with role="alert"', () => {
    const fixture = TestBed.createComponent(HostComponent);
    fixture.detectChanges();

    const alert = fixture.nativeElement.querySelector('[role="alert"]') as HTMLElement;
    expect(alert).toBeTruthy();
    expect(alert.textContent).toContain('No pudimos cargar los datos.');
  });

  it('renders detail when provided', () => {
    const fixture = TestBed.createComponent(HostComponent);
    fixture.componentInstance.detail = 'La reserva ya fue cancelada.';
    fixture.detectChanges();

    const alert = fixture.nativeElement.querySelector('[role="alert"]') as HTMLElement;
    expect(alert.textContent).toContain('La reserva ya fue cancelada.');
  });

  it('omits detail when absent', () => {
    const fixture = TestBed.createComponent(HostComponent);
    fixture.detectChanges();

    const text = fixture.nativeElement.textContent as string;
    expect(text.trim()).toBe('No pudimos cargar los datos.');
  });

  it('does not show a retry button unless a retry handler is bound via showRetry', () => {
    const fixture = TestBed.createComponent(HostComponent);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('ion-button')).toBeNull();
  });

  it('shows a retry button when showRetry is true', () => {
    const fixture = TestBed.createComponent(HostComponent);
    fixture.componentInstance.showRetry = true;
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('ion-button')).toBeTruthy();
  });

  it('emits retry when the retry button is clicked', () => {
    const fixture = TestBed.createComponent(HostComponent);
    fixture.componentInstance.showRetry = true;
    fixture.detectChanges();

    const button = fixture.nativeElement.querySelector('ion-button') as HTMLElement;
    button.click();

    expect(fixture.componentInstance.retried).toBe(1);
  });

  it('emits retry via the component output directly', () => {
    const fixture = TestBed.createComponent(AppErrorStateComponent);
    fixture.componentInstance.title = 'Error';
    fixture.componentInstance.showRetry = true;
    fixture.detectChanges();

    const handler = vi.fn();
    fixture.componentInstance.retry.subscribe(handler);

    (fixture.nativeElement.querySelector('ion-button') as HTMLElement).click();

    expect(handler).toHaveBeenCalledTimes(1);
  });
});
