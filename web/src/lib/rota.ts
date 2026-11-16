import type { Shift, ShiftInput, StaffMember } from './api';
import { addDays, hoursBetween, isLocalDate, isLocalTime, utcToZoned, weekDates, zonedToUtc } from './time';
import type { LocalDate, LocalTime } from './time';

export interface RotaDay {
  date: LocalDate;
  shifts: Shift[];
}

/** The week's shifts under the day they start on, by the business's clock, earliest first. */
export function shiftsByDay(shifts: Shift[], monday: LocalDate, zone: string): RotaDay[] {
  const days = weekDates(monday).map((date) => ({ date, shifts: [] as Shift[] }));
  const byDate = new Map(days.map((day) => [day.date, day]));
  for (const shift of shifts) {
    byDate.get(utcToZoned(shift.start, zone).date)?.shifts.push(shift);
  }
  for (const day of days) {
    day.shifts.sort(
      (a, b) =>
        new Date(a.start).getTime() - new Date(b.start).getTime() ||
        (a.staffName ?? '￿').localeCompare(b.staffName ?? '￿'),
    );
  }
  return days;
}

/** "09:00–17:00", or "22:00–06:00 +1" when it ends the next day. */
export function shiftTimes(shift: Pick<Shift, 'start' | 'end'>, zone: string): string {
  const start = utcToZoned(shift.start, zone);
  const end = utcToZoned(shift.end, zone);
  const nextDay = end.date !== start.date && !(end.time === '00:00' && end.date === addDays(start.date, 1));
  return `${start.time}–${end.time}${nextDay ? ' +1' : ''}`;
}

export interface PersonHours {
  id: string;
  name: string;
  hours: number;
  limitHours: number;
}

/** Everyone's hours this week next to their weekly limit, by name. */
export function hoursByPerson(shifts: Shift[], staff: StaffMember[]): PersonHours[] {
  const hours = new Map<string, number>();
  for (const shift of shifts) {
    if (shift.staffMemberId) {
      hours.set(shift.staffMemberId, (hours.get(shift.staffMemberId) ?? 0) + hoursBetween(shift.start, shift.end));
    }
  }
  return staff
    .map((person) => ({
      id: person.id,
      name: person.name,
      hours: hours.get(person.id) ?? 0,
      limitHours: person.weeklyLimitHours,
    }))
    .sort((a, b) => a.name.localeCompare(b.name));
}

export function openShiftCount(shifts: Shift[]): number {
  return shifts.filter((shift) => !shift.staffMemberId).length;
}

/** A shift as the manager types it: a day, two clock times, and maybe a person. */
export interface ShiftDraft {
  date: LocalDate;
  start: LocalTime;
  end: LocalTime;
  staffMemberId: string;
}

export function draftFromShift(shift: Shift, zone: string): ShiftDraft {
  const start = utcToZoned(shift.start, zone);
  return {
    date: start.date,
    start: start.time,
    end: utcToZoned(shift.end, zone).time,
    staffMemberId: shift.staffMemberId ?? '',
  };
}

export type DraftResult = { ok: true; input: ShiftInput } | { ok: false; error: string };

/**
 * The API request for a draft. An end at or before the start means the shift runs past midnight, so 22:00 to
 * 06:00 is a night shift. The API checks the rules that need the rota, like availability and hours.
 */
export function shiftInputFromDraft(draft: ShiftDraft, zone: string): DraftResult {
  if (!isLocalDate(draft.date)) {
    return { ok: false, error: 'Pick a day for the shift.' };
  }
  if (!isLocalTime(draft.start) || !isLocalTime(draft.end)) {
    return { ok: false, error: 'Enter the start and end as times, like 09:00.' };
  }
  if (draft.start === draft.end) {
    return { ok: false, error: 'The shift ends when it starts. Change one of the times.' };
  }

  const endDate = draft.end < draft.start ? addDays(draft.date, 1) : draft.date;
  return {
    ok: true,
    input: {
      start: zonedToUtc(draft.date, draft.start, zone).toISOString(),
      end: zonedToUtc(endDate, draft.end, zone).toISOString(),
      staffMemberId: draft.staffMemberId || null,
    },
  };
}
