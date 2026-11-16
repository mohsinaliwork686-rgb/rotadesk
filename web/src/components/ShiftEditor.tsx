import { useEffect, useState } from 'react';
import type { FormEvent } from 'react';

import { ApiError, api, describeError } from '../lib/api';
import type { AvailabilityWindow, Shift, StaffMember, Violation } from '../lib/api';
import { DAYS, describeWindow } from '../lib/availability';
import { shiftInputFromDraft } from '../lib/rota';
import type { ShiftDraft } from '../lib/rota';
import { formatDay, isLocalTime, weekDates, weekdayIndex, zoneLabel } from '../lib/time';
import type { LocalDate } from '../lib/time';

/** The shift being changed, or null for a new one, and what the form starts with. */
export interface Editing {
  shift: Shift | null;
  draft: ShiftDraft;
}

interface Props {
  editing: Editing;
  monday: LocalDate;
  zone: string;
  staff: StaffMember[];
  onClose: () => void;
  onSaved: () => Promise<void>;
}

export function ShiftEditor({ editing, monday, zone, staff, onClose, onSaved }: Props) {
  const [draft, setDraft] = useState(editing.draft);
  const [error, setError] = useState<string | null>(null);
  const [violations, setViolations] = useState<Violation[]>([]);
  const [busy, setBusy] = useState(false);
  const [availability, setAvailability] = useState<{ staffId: string; windows: AvailabilityWindow[] } | null>(null);

  const staffId = draft.staffMemberId;
  useEffect(() => {
    if (!staffId) {
      return;
    }
    let live = true;
    api.staffAvailability(staffId).then(
      (windows) => live && setAvailability({ staffId, windows }),
      () => undefined,
    );
    return () => {
      live = false;
    };
  }, [staffId]);

  const update = (patch: Partial<ShiftDraft>) => setDraft((current) => ({ ...current, ...patch }));

  function fail(e: unknown) {
    setError(describeError(e));
    setViolations(e instanceof ApiError && e.violations.length > 1 ? e.violations : []);
    setBusy(false);
  }

  async function save(event: FormEvent) {
    event.preventDefault();
    const result = shiftInputFromDraft(draft, zone);
    if (!result.ok) {
      setError(result.error);
      setViolations([]);
      return;
    }
    setBusy(true);
    setError(null);
    setViolations([]);
    try {
      if (editing.shift) {
        await api.updateShift(editing.shift.id, result.input);
      } else {
        await api.createShift(result.input);
      }
      await onSaved();
    } catch (e) {
      fail(e);
    }
  }

  async function remove() {
    if (!editing.shift || !window.confirm('Delete this shift?')) {
      return;
    }
    setBusy(true);
    setError(null);
    try {
      await api.deleteShift(editing.shift.id);
      await onSaved();
    } catch (e) {
      fail(e);
    }
  }

  const overnight = isLocalTime(draft.start) && isLocalTime(draft.end) && draft.end < draft.start;
  const day = DAYS[weekdayIndex(draft.date)];
  const allWindows = staffId && availability?.staffId === staffId ? availability.windows : null;
  const person = staff.find((s) => s.id === staffId);
  let availabilityHint: string | null = null;
  if (person && allWindows) {
    const windows = allWindows.filter((w) => w.day === day);
    availabilityHint =
      allWindows.length === 0
        ? `${person.name} hasn't set any availability.`
        : windows.length === 0
          ? `${person.name} isn't free on ${day}s.`
          : `${person.name} is free on ${day}s ${windows.map(describeWindow).join(', ')}.`;
  }

  return (
    <div className="overlay" onKeyDown={(e) => e.key === 'Escape' && onClose()}>
      <form className="card dialog stack" role="dialog" aria-modal="true" aria-labelledby="shift-heading" onSubmit={save}>
        <h2 id="shift-heading">{editing.shift ? 'Edit shift' : 'Add shift'}</h2>
        <p className="muted">Times are on the business clock ({zoneLabel(zone)}).</p>

        <label>
          Day
          <select value={draft.date} onChange={(e) => update({ date: e.target.value })}>
            {weekDates(monday).map((date) => (
              <option key={date} value={date}>
                {formatDay(date)}
              </option>
            ))}
          </select>
        </label>

        <div className="row">
          <label>
            Starts
            <input type="time" required value={draft.start} onChange={(e) => update({ start: e.target.value })} />
          </label>
          <label>
            Ends
            <input type="time" required value={draft.end} onChange={(e) => update({ end: e.target.value })} />
          </label>
        </div>
        {overnight && <p className="hint">Ends the next morning.</p>}

        <label>
          Who
          <select value={draft.staffMemberId} onChange={(e) => update({ staffMemberId: e.target.value })}>
            <option value="">No one yet (open shift)</option>
            {staff.map((member) => (
              <option key={member.id} value={member.id}>
                {member.name} · {member.role}
              </option>
            ))}
          </select>
        </label>
        {availabilityHint && <p className="hint">{availabilityHint}</p>}

        {error && (
          <div className="error" role="alert">
            <p>{error}</p>
            {violations.length > 0 && (
              <ul>
                {violations.slice(1).map((v) => (
                  <li key={v.code}>{v.message}</li>
                ))}
              </ul>
            )}
          </div>
        )}

        <div className="dialog-actions">
          {editing.shift && (
            <button type="button" className="danger" disabled={busy} onClick={() => void remove()}>
              Delete
            </button>
          )}
          <span className="spacer" />
          <button type="button" disabled={busy} onClick={onClose}>
            Cancel
          </button>
          <button type="submit" className="primary" disabled={busy}>
            {editing.shift ? 'Save' : 'Add shift'}
          </button>
        </div>
      </form>
    </div>
  );
}
