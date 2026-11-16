import { useEffect, useState } from 'react';
import type { FormEvent } from 'react';
import { Navigate, useLocation } from 'react-router';

import { api, describeError } from '../lib/api';
import type { DemoAccount } from '../lib/api';
import { useSession } from '../session';

function groupByBusiness(accounts: DemoAccount[]): [string, DemoAccount[]][] {
  const groups = new Map<string, DemoAccount[]>();
  for (const account of accounts) {
    groups.set(account.business, [...(groups.get(account.business) ?? []), account]);
  }
  return [...groups];
}

export function SignInPage() {
  const { state, refresh } = useSession();
  const location = useLocation();
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [demoAccounts, setDemoAccounts] = useState<DemoAccount[]>([]);

  useEffect(() => {
    let live = true;
    // The demo accounts exist only on the demo; anywhere else the call fails and the panel stays hidden.
    api.demoAccounts().then(
      (accounts) => live && setDemoAccounts(accounts),
      () => undefined,
    );
    return () => {
      live = false;
    };
  }, []);

  if (state.status === 'signed-in') {
    const from = (location.state as { from?: string } | null)?.from;
    return <Navigate to={from && from !== '/sign-in' ? from : '/rota'} replace />;
  }

  async function run(signIn: () => Promise<void>) {
    setBusy(true);
    setError(null);
    try {
      await signIn();
      await refresh();
    } catch (e) {
      setError(describeError(e));
      setBusy(false);
    }
  }

  function submit(event: FormEvent) {
    event.preventDefault();
    void run(() => api.signIn(email.trim(), password));
  }

  return (
    <div className="sign-in">
      <section className="card sign-in-card">
        <div className="sign-in-brand">
          <span className="logo" aria-hidden="true" />
          <h1>Rotadesk</h1>
        </div>
        <p className="muted">Staff rotas for small businesses.</p>

        <form onSubmit={submit} className="stack">
          <label>
            Email
            <input type="email" autoComplete="username" required value={email} onChange={(e) => setEmail(e.target.value)} />
          </label>
          <label>
            Password
            <input
              type="password"
              autoComplete="current-password"
              required
              value={password}
              onChange={(e) => setPassword(e.target.value)}
            />
          </label>
          {error && (
            <p className="error" role="alert">
              {error}
            </p>
          )}
          <button type="submit" className="primary" disabled={busy}>
            Sign in
          </button>
        </form>
      </section>

      {demoAccounts.length > 0 && (
        <section className="card demo" aria-labelledby="demo-heading">
          <h2 id="demo-heading">Try the demo</h2>
          <p className="muted">
            Two made-up businesses in different time zones. Sign in as anyone to see the rota as they would.
          </p>
          {groupByBusiness(demoAccounts).map(([business, people]) => (
            <div key={business} className="demo-business">
              <h3>{business}</h3>
              <ul className="demo-people">
                {people.map((person) => (
                  <li key={person.email}>
                    <button type="button" disabled={busy} onClick={() => void run(() => api.demoSignIn(person.email))}>
                      <span>{person.name}</span>
                      <span className="role">{person.role}</span>
                    </button>
                  </li>
                ))}
              </ul>
            </div>
          ))}
        </section>
      )}
    </div>
  );
}
