import { useState } from 'react';
import type { FormEvent } from 'react';

import { ApiError, api, describeError } from '../lib/api';
import type { Shift, StaffMember, Violation } from '../lib/api';
import { describeSwapShift, returnOptions } from '../lib/swaps';

interface Props {
  shift: Shift;
  /** The week's shifts, to choose one of the colleague's in return. */
  week: Shift[];
  /** Everyone the shift could go to: the staff list without the person offering it. */
  staff: StaffMember[];
  zone: string;
  onClose: () => void;
  onSent: (colleague: string) => void;
}

/** Offer one of your shifts to a colleague, maybe for one of theirs. They answer it, then a manager decides it. */
export function SwapOffer({ shift, week, staff, zone, onClose, onSent }: Props) {
  const [colleagueId, setColleagueId] = useState('');
  const [returnShiftId, setReturnShiftId] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [violations, setViolations] = useState<Violation[]>([]);
  const [busy, setBusy] = useState(false);

  const colleague = staff.find((person) => person.id === colleagueId);
  const theirs = colleagueId ? returnOptions(week, colleagueId) : [];

  async function send(event: FormEvent) {
    event.preventDefault();
    if (!colleague) {
      setError('Choose who to offer it to.');
      return;
    }
    setBusy(true);
    setError(null);
    setViolations([]);
    try {
      await api.createSwap({ offeredShiftId: shift.id, colleagueId: colleague.id, returnShiftId: returnShiftId || null });
      onSent(colleague.name);
    } catch (e) {
      setError(describeError(e));
      setViolations(e instanceof ApiError && e.violations.length > 1 ? e.violations : []);
      setBusy(false);
    }
  }

  return (
    <div className="overlay" onKeyDown={(e) => e.key === 'Escape' && onClose()}>
      <form className="card dialog stack" role="dialog" aria-modal="true" aria-labelledby="offer-heading" onSubmit={send}>
        <h2 id="offer-heading">Offer your shift</h2>
        <p className="muted">{describeSwapShift(shift, zone)}</p>

        <label>
          To
          <select
            value={colleagueId}
            onChange={(e) => {
              setColleagueId(e.target.value);
              setReturnShiftId('');
            }}
          >
            <option value="">Choose a colleague</option>
            {staff.map((person) => (
              <option key={person.id} value={person.id}>
                {person.name}
              </option>
            ))}
          </select>
        </label>

        {colleague && (
          <label>
            In return
            <select value={returnShiftId} onChange={(e) => setReturnShiftId(e.target.value)}>
              <option value="">Nothing, {colleague.name} just takes it</option>
              {theirs.map((s) => (
                <option key={s.id} value={s.id}>
                  {colleague.name}'s {describeSwapShift(s, zone)}
                </option>
              ))}
            </select>
          </label>
        )}

        <p className="hint">They answer first, then a manager approves it. The rota changes only then.</p>

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
          <span className="spacer" />
          <button type="button" disabled={busy} onClick={onClose}>
            Cancel
          </button>
          <button type="submit" className="primary" disabled={busy}>
            Send offer
          </button>
        </div>
      </form>
    </div>
  );
}
