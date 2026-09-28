import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';

import { AppShellComponent } from './app-shell.component';

@Component({
  standalone: true,
  imports: [AppShellComponent],
  template: `
    <app-shell title="Test title">
      <p>Projected content</p>
    </app-shell>
  `
})
class HostComponent {}

describe('AppShellComponent', () => {
  it('renders its title and projects content into the content area', async () => {
    const fixture = TestBed.createComponent(HostComponent);
    fixture.detectChanges();
    await fixture.whenStable();

    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('Test title');
    expect(text).toContain('Projected content');
  });
});
