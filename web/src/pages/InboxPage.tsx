import { useCallback, useEffect, useState } from 'react';

import { api, describeError } from '../lib/api';
import type { InboxEmail } from '../lib/api';
import { formatDay, utcToZoned } from '../lib/time';
import { useMe } from '../session';

/** The demo sends no real email: everything Rotadesk would send this person lands here instead. */
export function InboxPage() {
  const me = useMe();
  const [emails, setEmails] = useState<InboxEmail[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [open, setOpen] = useState<string | null>(null);

  const load = useCallback(async () => {
    try {
      setEmails(await api.inbox());
      setError(null);
    } catch (e) {
      setError(describeError(e));
    }
  }, []);

  useEffect(() => {
    let live = true;
    api.inbox().then(
      (loaded) => live && setEmails(loaded),
      (e: unknown) => live && setError(describeError(e)),
    );
    return () => {
      live = false;
    };
  }, []);

  const zone = me.business.timeZone;

  return (
    <div className="inbox">
      <div className="inbox-head">
        <h1>Inbox</h1>
        <button type="button" onClick={() => void load()}>
          Refresh
        </button>
      </div>
      <p className="muted">
        The demo sends no real email. What Rotadesk would send to {me.email} shows up here, a few seconds after the
        change that caused it.
      </p>

      {error && (
        <p className="error" role="alert">
          {error}
        </p>
      )}
      {!emails ? (
        !error && <p className="page-message">Loading your inbox…</p>
      ) : emails.length === 0 ? (
        <p className="card page-message">
          Nothing yet. Publishing a rota, changing a published shift or asking for a swap sends one.
        </p>
      ) : (
        <ul className="emails">
          {emails.map((email) => {
            const at = utcToZoned(email.deliveredAt, zone);
            const expanded = open === email.id;
            return (
              <li key={email.id} className="card email">
                <button
                  type="button"
                  className="email-head"
                  aria-expanded={expanded}
                  onClick={() => setOpen(expanded ? null : email.id)}
                >
                  <span className="email-subject">{email.subject}</span>
                  <span className="muted">
                    {formatDay(at.date)}, {at.time}
                  </span>
                </button>
                {expanded && <pre className="email-body">{email.body}</pre>}
              </li>
            );
          })}
        </ul>
      )}
    </div>
  );
}
