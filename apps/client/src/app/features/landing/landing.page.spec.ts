import { TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';

import { LandingPage } from './landing.page';

describe('LandingPage', () => {
  it('renders the product shell as the real landing, not the old health screen', async () => {
    const fixture = TestBed.createComponent(LandingPage);
    fixture.detectChanges();
    await fixture.whenStable();

    const text = fixture.nativeElement.textContent as string;

    expect(text).toContain('Residential Amenities Platform');
    expect(text).not.toContain('Check API');
    expect(text).not.toContain('not checked');
  });
});
