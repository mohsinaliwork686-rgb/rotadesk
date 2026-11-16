import { describe, expect, it } from 'vitest';

import {
  addDays,
  formatDay,
  formatHours,
  formatWeek,
  hoursBetween,
  isLocalDate,
  isMonday,
  mondayOf,
  today,
  utcToZoned,
  weekDates,
  weekdayIndex,
  zoneLabel,
  zonedToUtc,
} from './time';

const LONDON = 'Europe/London';
const KARACHI = 'Asia/Karachi';

describe('the test machine', () => {
  it('runs in Auckland, so nothing here can lean on its time zone', () => {
    expect(new Date(2026, 0, 1).getTimezoneOffset()).toBe(-13 * 60);
  });
});

describe('zonedToUtc', () => {
  it('reads a London winter time as UTC', () => {
    expect(zonedToUtc('2026-11-16', '09:00', LONDON).toISOString()).toBe('2026-11-16T09:00:00.000Z');
  });

  it('reads a London summer time an hour earlier in UTC', () => {
    expect(zonedToUtc('2026-07-01', '09:00', LONDON).toISOString()).toBe('2026-07-01T08:00:00.000Z');
  });

  it('reads Karachi five hours ahead of UTC', () => {
    expect(zonedToUtc('2026-11-16', '09:00', KARACHI).toISOString()).toBe('2026-11-16T04:00:00.000Z');
  });

  it('moves a time the clocks skip to the first one that exists', () => {
    // London goes from 01:00 straight to 02:00 on 29 March 2026.
    expect(zonedToUtc('2026-03-29', '01:30', LONDON).toISOString()).toBe('2026-03-29T01:00:00.000Z');
  });

  it('picks the first of a time that happens twice', () => {
    // On 25 October 2026 London shows 01:30 at 00:30 UTC, then again at 01:30 UTC.
    expect(zonedToUtc('2026-10-25', '01:30', LONDON).toISOString()).toBe('2026-10-25T00:30:00.000Z');
  });

  it('handles a zone whose clocks skip midnight', () => {
    // Chile starts summer time at midnight on 6 September 2026, so that day begins at 01:00.
    expect(zonedToUtc('2026-09-06', '00:00', 'America/Santiago').toISOString()).toBe('2026-09-06T04:00:00.000Z');
  });

  it('refuses text that is not a date or a time', () => {
    expect(() => zonedToUtc('2026-02-30', '09:00', LONDON)).toThrow(RangeError);
    expect(() => zonedToUtc('2026-11-16', '9am', LONDON)).toThrow(RangeError);
  });
});

describe('utcToZoned', () => {
  it('shows an instant on the business clock', () => {
    expect(utcToZoned('2026-11-16T09:00:00Z', LONDON)).toEqual({ date: '2026-11-16', time: '09:00' });
    expect(utcToZoned('2026-07-01T08:00:00Z', LONDON)).toEqual({ date: '2026-07-01', time: '09:00' });
  });

  it('moves to the next day when the business is already past midnight', () => {
    expect(utcToZoned('2026-11-16T20:00:00Z', KARACHI)).toEqual({ date: '2026-11-17', time: '01:00' });
  });

  it('shows both instants of a repeated hour as the same wall time', () => {
    expect(utcToZoned('2026-10-25T00:30:00Z', LONDON).time).toBe('01:30');
    expect(utcToZoned('2026-10-25T01:30:00Z', LONDON).time).toBe('01:30');
  });

  it('round-trips with zonedToUtc', () => {
    for (const zone of [LONDON, KARACHI, 'America/New_York', 'Australia/Lord_Howe']) {
      const instant = zonedToUtc('2026-06-15', '17:45', zone);
      expect(utcToZoned(instant, zone)).toEqual({ date: '2026-06-15', time: '17:45' });
    }
  });
});

describe('today', () => {
  it('is the business date, not the machine date', () => {
    const now = new Date('2026-11-16T20:00:00Z');
    expect(today(LONDON, now)).toBe('2026-11-16');
    expect(today(KARACHI, now)).toBe('2026-11-17');
  });
});

describe('calendar dates', () => {
  it('adds days across months and years', () => {
    expect(addDays('2026-11-30', 1)).toBe('2026-12-01');
    expect(addDays('2026-12-28', 7)).toBe('2027-01-04');
    expect(addDays('2026-03-01', -1)).toBe('2026-02-28');
  });

  it('starts weeks on Monday', () => {
    expect(weekdayIndex('2026-11-16')).toBe(0);
    expect(weekdayIndex('2026-11-22')).toBe(6);
    expect(mondayOf('2026-11-22')).toBe('2026-11-16');
    expect(mondayOf('2026-11-16')).toBe('2026-11-16');
    expect(isMonday('2026-11-16')).toBe(true);
    expect(isMonday('2026-11-17')).toBe(false);
  });

  it('lists the seven days of a week', () => {
    expect(weekDates('2026-11-30')).toEqual([
      '2026-11-30',
      '2026-12-01',
      '2026-12-02',
      '2026-12-03',
      '2026-12-04',
      '2026-12-05',
      '2026-12-06',
    ]);
  });

  it('knows which dates exist', () => {
    expect(isLocalDate('2028-02-29')).toBe(true);
    expect(isLocalDate('2026-02-29')).toBe(false);
    expect(isLocalDate('16/11/2026')).toBe(false);
  });
});

describe('formatting', () => {
  it('names a day', () => {
    expect(formatDay('2026-11-16')).toBe('Mon 16 Nov');
  });

  it('names a week inside one month, across two, and across a new year', () => {
    expect(formatWeek('2026-11-16')).toBe('16–22 Nov 2026');
    expect(formatWeek('2026-11-30')).toBe('30 Nov – 6 Dec 2026');
    expect(formatWeek('2026-12-28')).toBe('28 Dec 2026 – 3 Jan 2027');
  });

  it('counts the hours really worked over a clock change', () => {
    const start = zonedToUtc('2026-10-24', '22:00', LONDON);
    const end = zonedToUtc('2026-10-25', '06:00', LONDON);
    expect(hoursBetween(start, end)).toBe(9);
  });

  it('names the clock by its city', () => {
    expect(zoneLabel('Europe/London')).toBe('London time');
    expect(zoneLabel('America/Argentina/Buenos_Aires')).toBe('Buenos Aires time');
  });

  it('writes hours without trailing zeros', () => {
    expect(formatHours(8)).toBe('8 h');
    expect(formatHours(7.5)).toBe('7.5 h');
  });
});
