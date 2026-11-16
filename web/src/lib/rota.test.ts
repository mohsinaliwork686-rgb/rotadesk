import { describe, expect, it } from 'vitest';

import type { Shift, StaffMember } from './api';
import { draftFromShift, hoursByPerson, openShiftCount, shiftInputFromDraft, shiftTimes, shiftsByDay } from './rota';

const LONDON = 'Europe/London';
const KARACHI = 'Asia/Karachi';

function shift(start: string, end: string, staffMemberId: string | null = null, staffName: string | null = null): Shift {
  return { id: `${start}-${staffMemberId}`, start, end, staffMemberId, staffName, warnings: [] };
}

const amira: StaffMember = { id: 'amira', name: 'Amira', email: 'amira@northside.example', role: 'Staff', weeklyLimitHours: 20 };
const ben: StaffMember = { id: 'ben', name: 'Ben', email: 'ben@northside.example', role: 'Staff', weeklyLimitHours: 40 };

describe('shiftsByDay', () => {
  it('puts each shift under the business day it starts on, earliest first', () => {
    const late = shift('2026-11-16T14:00:00Z', '2026-11-16T22:00:00Z', 'ben', 'Ben');
    const early = shift('2026-11-16T08:00:00Z', '2026-11-16T16:00:00Z', 'amira', 'Amira');
    const sunday = shift('2026-11-22T10:00:00Z', '2026-11-22T18:00:00Z');

    const days = shiftsByDay([late, sunday, early], '2026-11-16', LONDON);

    expect(days.map((d) => d.date)).toEqual([
      '2026-11-16',
      '2026-11-17',
      '2026-11-18',
      '2026-11-19',
      '2026-11-20',
      '2026-11-21',
      '2026-11-22',
    ]);
    expect(days[0]?.shifts).toEqual([early, late]);
    expect(days[6]?.shifts).toEqual([sunday]);
  });

  it('uses the business clock, so a late UTC shift can belong to the next day', () => {
    // 20:00 UTC on Monday is 01:00 on Tuesday in Karachi.
    const night = shift('2026-11-16T20:00:00Z', '2026-11-17T02:00:00Z');

    const days = shiftsByDay([night], '2026-11-16', KARACHI);

    expect(days[0]?.shifts).toEqual([]);
    expect(days[1]?.shifts).toEqual([night]);
  });

  it('lists named shifts before open ones that start at the same time', () => {
    const open = shift('2026-11-16T09:00:00Z', '2026-11-16T17:00:00Z');
    const named = shift('2026-11-16T09:00:00Z', '2026-11-16T17:00:00Z', 'amira', 'Amira');

    expect(shiftsByDay([open, named], '2026-11-16', LONDON)[0]?.shifts).toEqual([named, open]);
  });
});

describe('shiftTimes', () => {
  it('shows a day shift on the business clock', () => {
    expect(shiftTimes(shift('2026-07-01T08:00:00Z', '2026-07-01T16:00:00Z'), LONDON)).toBe('09:00–17:00');
  });

  it('marks a shift that ends the next day', () => {
    expect(shiftTimes(shift('2026-11-16T22:00:00Z', '2026-11-17T06:00:00Z'), LONDON)).toBe('22:00–06:00 +1');
  });

  it('does not mark a shift that ends at midnight', () => {
    expect(shiftTimes(shift('2026-11-16T18:00:00Z', '2026-11-17T00:00:00Z'), LONDON)).toBe('18:00–00:00');
  });
});

describe('hoursByPerson', () => {
  it('adds up each person against their limit, including people with no shifts', () => {
    const shifts = [
      shift('2026-11-16T09:00:00Z', '2026-11-16T17:00:00Z', 'amira', 'Amira'),
      shift('2026-11-17T09:00:00Z', '2026-11-17T13:30:00Z', 'amira', 'Amira'),
      shift('2026-11-18T09:00:00Z', '2026-11-18T17:00:00Z'),
    ];

    expect(hoursByPerson(shifts, [ben, amira])).toEqual([
      { id: 'amira', name: 'Amira', hours: 12.5, limitHours: 20 },
      { id: 'ben', name: 'Ben', hours: 0, limitHours: 40 },
    ]);
    expect(openShiftCount(shifts)).toBe(1);
  });
});

describe('shiftInputFromDraft', () => {
  it('turns a day and two clock times into UTC instants', () => {
    expect(shiftInputFromDraft({ date: '2026-07-01', start: '09:00', end: '17:00', staffMemberId: 'amira' }, LONDON)).toEqual({
      ok: true,
      input: { start: '2026-07-01T08:00:00.000Z', end: '2026-07-01T16:00:00.000Z', staffMemberId: 'amira' },
    });
  });

  it('reads an end before the start as the next morning', () => {
    const result = shiftInputFromDraft({ date: '2026-11-16', start: '22:00', end: '06:00', staffMemberId: '' }, LONDON);

    expect(result).toEqual({
      ok: true,
      input: { start: '2026-11-16T22:00:00.000Z', end: '2026-11-17T06:00:00.000Z', staffMemberId: null },
    });
  });

  it('keeps a night shift over the clocks going back at its real length', () => {
    const result = shiftInputFromDraft({ date: '2026-10-24', start: '22:00', end: '06:00', staffMemberId: '' }, LONDON);

    expect(result.ok && result.input).toEqual({
      start: '2026-10-24T21:00:00.000Z',
      end: '2026-10-25T06:00:00.000Z',
      staffMemberId: null,
    });
  });

  it('refuses a shift that ends when it starts, or times it cannot read', () => {
    expect(shiftInputFromDraft({ date: '2026-11-16', start: '09:00', end: '09:00', staffMemberId: '' }, LONDON).ok).toBe(false);
    expect(shiftInputFromDraft({ date: '2026-11-16', start: '', end: '17:00', staffMemberId: '' }, LONDON).ok).toBe(false);
    expect(shiftInputFromDraft({ date: '', start: '09:00', end: '17:00', staffMemberId: '' }, LONDON).ok).toBe(false);
  });

  it('round-trips with draftFromShift', () => {
    const night = shift('2026-11-16T20:00:00Z', '2026-11-17T02:00:00Z', 'amira', 'Amira');

    const draft = draftFromShift(night, KARACHI);

    expect(draft).toEqual({ date: '2026-11-17', start: '01:00', end: '07:00', staffMemberId: 'amira' });
    expect(shiftInputFromDraft(draft, KARACHI)).toEqual({
      ok: true,
      input: { start: '2026-11-16T20:00:00.000Z', end: '2026-11-17T02:00:00.000Z', staffMemberId: 'amira' },
    });
  });
});
