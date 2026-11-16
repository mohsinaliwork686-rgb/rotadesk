import { describe, expect, it } from 'vitest';

import { DAYS, byDay, describeWindow, endForPicker, newDraft, normaliseEnd, windowProblem, windowsToSave } from './availability';

describe('the end of a window', () => {
  it('reads 00:00 from a time picker as until midnight, and back', () => {
    expect(normaliseEnd('00:00')).toBe('24:00');
    expect(normaliseEnd('17:00')).toBe('17:00');
    expect(endForPicker('24:00')).toBe('00:00');
  });
});

describe('windowProblem', () => {
  it('accepts a window inside one day, up to midnight', () => {
    expect(windowProblem({ from: '09:00', to: '17:00' })).toBeNull();
    expect(windowProblem({ from: '18:00', to: '24:00' })).toBeNull();
    expect(windowProblem({ from: '18:00', to: '00:00' })).toBeNull();
  });

  it('refuses a window that ends before it starts', () => {
    expect(windowProblem({ from: '17:00', to: '09:00' })).toMatch(/after the start/);
    expect(windowProblem({ from: '09:00', to: '09:00' })).toMatch(/after the start/);
  });

  it('refuses text that is not a time', () => {
    expect(windowProblem({ from: '9', to: '17:00' })).toMatch(/start time/);
    expect(windowProblem({ from: '09:00', to: '25:00' })).toMatch(/end time/);
    expect(windowProblem({ from: '24:00', to: '24:00' })).toMatch(/start time/);
  });
});

describe('windowsToSave', () => {
  it('sends each window with midnight written the way the API expects', () => {
    const drafts = [newDraft('Monday', '09:00', '17:00'), newDraft('Friday', '18:00', '00:00')];

    expect(windowsToSave(drafts)).toEqual({
      ok: true,
      windows: [
        { day: 'Monday', from: '09:00', to: '17:00' },
        { day: 'Friday', from: '18:00', to: '24:00' },
      ],
    });
  });

  it('names the rows that need fixing', () => {
    const good = newDraft('Monday');
    const bad = newDraft('Tuesday', '17:00', '09:00');

    const result = windowsToSave([good, bad]);

    expect(result.ok).toBe(false);
    expect(!result.ok && [...result.problems.keys()]).toEqual([bad.key]);
  });

  it('can clear every window', () => {
    expect(windowsToSave([])).toEqual({ ok: true, windows: [] });
  });
});

describe('byDay', () => {
  it('lists every day from Monday, each day earliest first', () => {
    const days = byDay([
      { day: 'Sunday', from: '10:00', to: '14:00' },
      { day: 'Monday', from: '13:00', to: '17:00' },
      { day: 'Monday', from: '08:00', to: '12:00' },
    ]);

    expect(days.map((d) => d.day)).toEqual(DAYS);
    expect(days[0]?.windows.map((w) => w.from)).toEqual(['08:00', '13:00']);
    expect(days[6]?.windows).toHaveLength(1);
    expect(days[3]?.windows).toEqual([]);
  });
});

describe('describeWindow', () => {
  it('reads like a person would say it', () => {
    expect(describeWindow({ from: '09:00', to: '17:00' })).toBe('09:00–17:00');
    expect(describeWindow({ from: '18:00', to: '24:00' })).toBe('18:00–midnight');
    expect(describeWindow({ from: '00:00', to: '24:00' })).toBe('All day');
  });
});
