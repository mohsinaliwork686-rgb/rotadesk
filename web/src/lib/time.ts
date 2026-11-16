// Rotas are read and written on the business's clock, whatever time zone the browser is in. A café in London
// shows 09:00 to a manager checking it from Karachi. Dates here are "YYYY-MM-DD" and times "HH:mm", both as that
// clock shows them; the API speaks in UTC instants.

export type LocalDate = string;
export type LocalTime = string;

export interface ZonedMoment {
  date: LocalDate;
  time: LocalTime;
}

const MINUTE = 60_000;
const DAY = 24 * 60 * MINUTE;

const DATE_PATTERN = /^(\d{4})-(\d{2})-(\d{2})$/;
const TIME_PATTERN = /^([01]\d|2[0-3]):([0-5]\d)$/;

const MONTHS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
const WEEKDAYS = ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'];

const formatters = new Map<string, Intl.DateTimeFormat>();

function wallClockFormatter(zone: string): Intl.DateTimeFormat {
  let formatter = formatters.get(zone);
  if (!formatter) {
    formatter = new Intl.DateTimeFormat('en-GB', {
      timeZone: zone,
      hourCycle: 'h23',
      year: 'numeric',
      month: '2-digit',
      day: '2-digit',
      hour: '2-digit',
      minute: '2-digit',
    });
    formatters.set(zone, formatter);
  }
  return formatter;
}

/** What the zone's clock shows at this instant, as if that wall-clock reading were UTC, to the minute. */
function wallClockAt(instant: number, zone: string): number {
  const parts = wallClockFormatter(zone).formatToParts(new Date(instant));
  const part = (type: Intl.DateTimeFormatPartTypes) => Number(parts.find((p) => p.type === type)?.value);
  return Date.UTC(part('year'), part('month') - 1, part('day'), part('hour'), part('minute'));
}

function parseDate(date: LocalDate): [number, number, number] {
  const match = DATE_PATTERN.exec(date);
  if (!match || !isLocalDate(date)) {
    throw new RangeError(`'${date}' isn't a date like 2026-11-16.`);
  }
  return [Number(match[1]), Number(match[2]), Number(match[3])];
}

function parseTime(time: LocalTime): [number, number] {
  const match = TIME_PATTERN.exec(time);
  if (!match) {
    throw new RangeError(`'${time}' isn't a time like 09:00.`);
  }
  return [Number(match[1]), Number(match[2])];
}

const pad = (n: number) => String(n).padStart(2, '0');

function toLocalDate(utcMidnight: number): LocalDate {
  const d = new Date(utcMidnight);
  return `${d.getUTCFullYear()}-${pad(d.getUTCMonth() + 1)}-${pad(d.getUTCDate())}`;
}

export function isLocalDate(text: string): boolean {
  const match = DATE_PATTERN.exec(text);
  if (!match) {
    return false;
  }
  const [year, month, day] = [Number(match[1]), Number(match[2]), Number(match[3])];
  const d = new Date(Date.UTC(year, month - 1, day));
  return d.getUTCFullYear() === year && d.getUTCMonth() === month - 1 && d.getUTCDate() === day;
}

export function isLocalTime(text: string): boolean {
  return TIME_PATTERN.test(text);
}

/** The instants at which the zone's clock reads this wall time: none in a gap, two when the clocks go back. */
function instantsFor(wall: number, zone: string): number[] {
  // At most one clock change happens within a day of any moment, so the offsets a day either side cover it.
  const offsets = new Set([wallClockAt(wall - DAY, zone) - (wall - DAY), wallClockAt(wall + DAY, zone) - (wall + DAY)]);
  return [...offsets]
    .map((offset) => wall - offset)
    .filter((instant) => wallClockAt(instant, zone) === wall)
    .sort((a, b) => a - b);
}

/**
 * The instant at which the business's clock shows this date and time.
 *
 * When the clocks go back the time happens twice; this picks the first. When they go forward it doesn't happen
 * at all, so it moves later half an hour at a time until it does, the same rule the API uses when it copies a
 * week.
 */
export function zonedToUtc(date: LocalDate, time: LocalTime, zone: string): Date {
  const [year, month, day] = parseDate(date);
  const [hour, minute] = parseTime(time);
  let wall = Date.UTC(year, month - 1, day, hour, minute);
  for (let tries = 0; tries < 4; tries++) {
    const [first] = instantsFor(wall, zone);
    if (first !== undefined) {
      return new Date(first);
    }
    wall += 30 * MINUTE;
  }
  throw new RangeError(`${date} ${time} doesn't exist in ${zone}.`);
}

/** The date and time the business's clock shows at this instant. */
export function utcToZoned(instant: string | Date, zone: string): ZonedMoment {
  const wall = wallClockAt(new Date(instant).getTime(), zone);
  const d = new Date(wall);
  return {
    date: toLocalDate(Date.UTC(d.getUTCFullYear(), d.getUTCMonth(), d.getUTCDate())),
    time: `${pad(d.getUTCHours())}:${pad(d.getUTCMinutes())}`,
  };
}

/** Today's date on the business's clock. */
export function today(zone: string, now: Date = new Date()): LocalDate {
  return utcToZoned(now, zone).date;
}

export function addDays(date: LocalDate, days: number): LocalDate {
  const [year, month, day] = parseDate(date);
  return toLocalDate(Date.UTC(year, month - 1, day + days));
}

/** 0 for Monday up to 6 for Sunday. */
export function weekdayIndex(date: LocalDate): number {
  const [year, month, day] = parseDate(date);
  return (new Date(Date.UTC(year, month - 1, day)).getUTCDay() + 6) % 7;
}

export function isMonday(date: LocalDate): boolean {
  return isLocalDate(date) && weekdayIndex(date) === 0;
}

/** The Monday that starts this date's week. */
export function mondayOf(date: LocalDate): LocalDate {
  return addDays(date, -weekdayIndex(date));
}

/** Monday to Sunday of the week starting on this Monday. */
export function weekDates(monday: LocalDate): LocalDate[] {
  return Array.from({ length: 7 }, (_, i) => addDays(monday, i));
}

/** "Mon 16 Nov" */
export function formatDay(date: LocalDate): string {
  const [, month, day] = parseDate(date);
  return `${WEEKDAYS[weekdayIndex(date)]} ${day} ${MONTHS[month - 1]}`;
}

/** "16–22 Nov 2026", "30 Nov – 6 Dec 2026" or "28 Dec 2026 – 3 Jan 2027" */
export function formatWeek(monday: LocalDate): string {
  const [startYear, startMonth, startDay] = parseDate(monday);
  const [endYear, endMonth, endDay] = parseDate(addDays(monday, 6));
  if (startYear !== endYear) {
    return `${startDay} ${MONTHS[startMonth - 1]} ${startYear} – ${endDay} ${MONTHS[endMonth - 1]} ${endYear}`;
  }
  if (startMonth !== endMonth) {
    return `${startDay} ${MONTHS[startMonth - 1]} – ${endDay} ${MONTHS[endMonth - 1]} ${endYear}`;
  }
  return `${startDay}–${endDay} ${MONTHS[endMonth - 1]} ${endYear}`;
}

/** Hours from one instant to the next, as really elapsed: a night shift over a clock change is an hour off. */
export function hoursBetween(start: string | Date, end: string | Date): number {
  return (new Date(end).getTime() - new Date(start).getTime()) / (60 * MINUTE);
}

/** "London time" for Europe/London, so people know which clock they're reading. */
export function zoneLabel(zone: string): string {
  const city = zone.split('/').pop() ?? zone;
  return `${city.replaceAll('_', ' ')} time`;
}

/** "8 h", "7.5 h" */
export function formatHours(hours: number): string {
  const rounded = Math.round(hours * 100) / 100;
  return `${rounded} h`;
}
