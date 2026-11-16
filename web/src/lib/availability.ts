import type { AvailabilityWindow, Day } from './api';
import { isLocalTime } from './time';

export const DAYS: readonly Day[] = ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday', 'Sunday'];

export const MIDNIGHT = '24:00';

/** A window being edited. `key` only tells the rows apart on screen. */
export interface WindowDraft extends AvailabilityWindow {
  key: string;
}

let nextKey = 0;

export function newDraft(day: Day, from = '09:00', to = '17:00'): WindowDraft {
  nextKey += 1;
  return { key: `w${nextKey}`, day, from, to };
}

export function draftsFrom(windows: AvailabilityWindow[]): WindowDraft[] {
  return windows.map((w) => newDraft(w.day, w.from, w.to));
}

/** A time picker can't show 24:00, so an end of 00:00 means until midnight. */
export function normaliseEnd(to: string): string {
  return to === '00:00' ? MIDNIGHT : to;
}

/** The end as a time picker can show it. */
export function endForPicker(to: string): string {
  return to === MIDNIGHT ? '00:00' : to;
}

function minutes(time: string): number {
  if (time === MIDNIGHT) {
    return 24 * 60;
  }
  const [hours, mins] = time.split(':').map(Number);
  return (hours ?? 0) * 60 + (mins ?? 0);
}

/** Why the window can't be saved, or null when it's fine. Windows stay within one day, like the API's. */
export function windowProblem(window: Pick<AvailabilityWindow, 'from' | 'to'>): string | null {
  const to = normaliseEnd(window.to);
  if (!isLocalTime(window.from)) {
    return 'Enter a start time like 09:00.';
  }
  if (to !== MIDNIGHT && !isLocalTime(to)) {
    return 'Enter an end time like 17:00.';
  }
  if (minutes(to) <= minutes(window.from)) {
    return 'The end has to be after the start. For a night, add a window on each day.';
  }
  return null;
}

export type SaveResult = { ok: true; windows: AvailabilityWindow[] } | { ok: false; problems: Map<string, string> };

/** The request body for saving, or each row's problem. The API merges windows that overlap or touch. */
export function windowsToSave(drafts: WindowDraft[]): SaveResult {
  const problems = new Map<string, string>();
  for (const draft of drafts) {
    const problem = windowProblem(draft);
    if (problem) {
      problems.set(draft.key, problem);
    }
  }
  if (problems.size > 0) {
    return { ok: false, problems };
  }
  return { ok: true, windows: drafts.map(({ day, from, to }) => ({ day, from, to: normaliseEnd(to) })) };
}

/** Each day's windows, Monday first, earliest first. */
export function byDay<T extends AvailabilityWindow>(windows: T[]): { day: Day; windows: T[] }[] {
  return DAYS.map((day) => ({
    day,
    windows: windows.filter((w) => w.day === day).sort((a, b) => minutes(a.from) - minutes(b.from)),
  }));
}

/** "09:00–17:00", "18:00–midnight" or "All day" */
export function describeWindow(window: Pick<AvailabilityWindow, 'from' | 'to'>): string {
  const to = normaliseEnd(window.to);
  if (window.from === '00:00' && to === MIDNIGHT) {
    return 'All day';
  }
  return `${window.from}–${to === MIDNIGHT ? 'midnight' : to}`;
}
