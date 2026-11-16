import { useEffect, useState } from 'react';

import { api, describeError } from '../lib/api';
import type { Day } from '../lib/api';
import { MIDNIGHT, byDay, draftsFrom, endForPicker, newDraft, normaliseEnd, windowsToSave } from '../lib/availability';
import type { WindowDraft } from '../lib/availability';
import { zoneLabel } from '../lib/time';
import { useMe } from '../session';

export function AvailabilityPage() {
  const me = useMe();
  const [drafts, setDrafts] = useState<WindowDraft[] | null>(null);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [problems, setProblems] = useState<Map<string, string>>(new Map());
  const [saveError, setSaveError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [dirty, setDirty] = useState(false);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    let live = true;
    api.myAvailability().then(
      (windows) => live && setDrafts(draftsFrom(windows)),
      (error: unknown) => live && setLoadError(describeError(error)),
    );
    return () => {
      live = false;
    };
  }, []);

  if (loadError) {
    return (
      <p className="error" role="alert">
        {loadError}
      </p>
    );
  }
  if (!drafts) {
    return <p className="page-message">Loading your availability…</p>;
  }

  function change(next: WindowDraft[]) {
    setDrafts(next);
    setDirty(true);
    setNotice(null);
  }

  const current = drafts;
  const update = (key: string, patch: Partial<WindowDraft>) =>
    change(current.map((d) => (d.key === key ? { ...d, ...patch } : d)));
  const add = (day: Day) => change([...current, newDraft(day)]);
  const remove = (key: string) => change(current.filter((d) => d.key !== key));

  async function save() {
    const result = windowsToSave(current);
    if (!result.ok) {
      setProblems(result.problems);
      return;
    }
    setProblems(new Map());
    setSaveError(null);
    setBusy(true);
    try {
      const saved = await api.saveMyAvailability(result.windows);
      setDrafts(draftsFrom(saved));
      setDirty(false);
      setNotice(saved.length < result.windows.length ? 'Saved. Times that overlapped were joined up.' : 'Saved.');
    } catch (error) {
      setSaveError(describeError(error));
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="availability">
      <h1>Your availability</h1>
      <p className="muted">
        When you can work each week, on {me.business.name}'s clock ({zoneLabel(me.business.timeZone)}). Managers see
        this while they build the rota, and a shift outside it gets a warning. Leave it empty and you won't get
        warnings at all.
      </p>

      <div className="card days">
        {byDay(current).map(({ day, windows }) => (
          <section key={day} className="availability-day" aria-label={day}>
            <h2>{day}</h2>
            <div className="windows">
              {windows.length === 0 && <p className="empty">Not available</p>}
              {windows.map((w) => (
                <div key={w.key} className="window">
                  <div className="row">
                    <label>
                      From
                      <input type="time" value={w.from} onChange={(e) => update(w.key, { from: e.target.value })} />
                    </label>
                    <label>
                      To
                      <input
                        type="time"
                        value={endForPicker(w.to)}
                        onChange={(e) => update(w.key, { to: e.target.value })}
                      />
                    </label>
                    <button
                      type="button"
                      className="link-button"
                      onClick={() => remove(w.key)}
                      aria-label={`Remove ${day} ${w.from}`}
                    >
                      Remove
                    </button>
                  </div>
                  {normaliseEnd(w.to) === MIDNIGHT && <p className="hint">Until midnight</p>}
                  {problems.get(w.key) && <p className="error">{problems.get(w.key)}</p>}
                </div>
              ))}
            </div>
            <button type="button" className="link-button" onClick={() => add(day)}>
              + Add time
            </button>
          </section>
        ))}
      </div>

      <div className="save-bar">
        <button type="button" className="primary" disabled={!dirty || busy} onClick={() => void save()}>
          Save
        </button>
        <span className="muted">An end of 00:00 means until midnight.</span>
        {notice && (
          <span className="notice" role="status">
            {notice}
          </span>
        )}
        {problems.size > 0 && (
          <span className="error" role="alert">
            Fix the times marked above.
          </span>
        )}
        {saveError && (
          <span className="error" role="alert">
            {saveError}
          </span>
        )}
      </div>
    </div>
  );
}
