import { useEffect, useState } from 'react';

import { ApiError, api, describeError } from '../lib/api';
import type { Me, Swap, SwapAction } from '../lib/api';
import { describeSwapShift, splitSwaps, swapActions, swapState } from '../lib/swaps';
import { formatDay, utcToZoned } from '../lib/time';
import { isManager, useMe } from '../session';

const ACTION_LABELS: Record<SwapAction, string> = {
  accept: 'Accept',
  decline: 'Decline',
  cancel: 'Cancel swap',
  approve: 'Approve',
  reject: 'Reject',
};

export function SwapsPage() {
  const me = useMe();
  const [swaps, setSwaps] = useState<Swap[] | null>(null);
  const [loadError, setLoadError] = useState<string | null>(null);

  useEffect(() => {
    let live = true;
    api.swaps().then(
      (loaded) => live && setSwaps(loaded),
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
  if (!swaps) {
    return <p className="page-message">Loading swaps…</p>;
  }

  const { open, done } = splitSwaps(swaps);
  const replace = (updated: Swap) => setSwaps((current) => (current ?? []).map((s) => (s.id === updated.id ? updated : s)));

  return (
    <div className="swaps">
      <h1>Swaps</h1>
      <p className="muted">
        {isManager(me)
          ? 'Every swap in the business. Once a colleague accepts, a manager who is not part of it approves or rejects it, and the rules are checked again.'
          : 'Swaps you asked for or were asked about. To offer a shift, click it on a published rota.'}
      </p>

      <section aria-labelledby="open-heading">
        <h2 id="open-heading">Waiting</h2>
        {open.length === 0 ? (
          <p className="empty">Nothing is waiting.</p>
        ) : (
          <ul className="swap-list">
            {open.map((swap) => (
              <li key={swap.id}>
                <SwapCard swap={swap} me={me} onChanged={replace} />
              </li>
            ))}
          </ul>
        )}
      </section>

      {done.length > 0 && (
        <section aria-labelledby="done-heading">
          <h2 id="done-heading">Finished</h2>
          <ul className="swap-list">
            {done.map((swap) => (
              <li key={swap.id}>
                <SwapCard swap={swap} me={me} onChanged={replace} />
              </li>
            ))}
          </ul>
        </section>
      )}
    </div>
  );
}

function SwapCard({ swap, me, onChanged }: { swap: Swap; me: Me; onChanged: (swap: Swap) => void }) {
  const zone = me.business.timeZone;
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const actions = swapActions(swap, me);
  const asked = utcToZoned(swap.createdAt, zone);
  const isMe = (person: { id: string }) => person.id === me.staffMemberId;

  async function act(action: SwapAction) {
    setBusy(true);
    setError(null);
    try {
      onChanged(await api.actOnSwap(swap.id, action));
    } catch (e) {
      const violations = e instanceof ApiError ? e.violations.slice(1).map((v) => v.message) : [];
      setError([describeError(e), ...violations].join(' '));
    } finally {
      setBusy(false);
    }
  }

  return (
    <article
      className={actions.length > 0 ? 'card swap needs-you' : 'card swap'}
      aria-label={`Swap from ${swap.requester.name} to ${swap.colleague.name}`}
    >
      <div className="swap-head">
        <span className={`swap-status ${swap.status.toLowerCase()}`}>{swapState(swap)}</span>
        {actions.length > 0 && <span className="needs-you-tag">Needs you</span>}
        <span className="muted swap-asked">
          Asked {formatDay(asked.date)} at {asked.time}
        </span>
      </div>
      <p className="swap-what">
        <strong>{isMe(swap.requester) ? 'You' : swap.requester.name}</strong> {isMe(swap.requester) ? 'offer' : 'offers'}{' '}
        {describeSwapShift(swap.offered, zone)} to <strong>{isMe(swap.colleague) ? 'you' : swap.colleague.name}</strong>
        {swap.return ? (
          <>
            , for {describeSwapShift(swap.return, zone)}
          </>
        ) : (
          <>, with nothing back</>
        )}
        .
      </p>
      {swap.warnings.map((warning) => (
        <p key={warning} className="shift-warning">
          {warning}
        </p>
      ))}
      {error && (
        <p className="error" role="alert">
          {error}
        </p>
      )}
      {actions.length > 0 && (
        <div className="swap-actions">
          {actions.map((action) => (
            <button
              key={action}
              type="button"
              className={action === 'accept' || action === 'approve' ? 'primary' : undefined}
              disabled={busy}
              onClick={() => void act(action)}
            >
              {ACTION_LABELS[action]}
            </button>
          ))}
        </div>
      )}
    </article>
  );
}
