import { useEffect, useState } from 'react';
import { NavLink, Outlet } from 'react-router';

import { api } from '../lib/api';
import { useMe, useSession } from '../session';
import { zoneLabel } from '../lib/time';

export function Layout() {
  const me = useMe();
  const { signOut } = useSession();
  const [demo, setDemo] = useState(false);

  // The inbox exists only on the demo, which is also the only place the demo accounts answer.
  useEffect(() => {
    let live = true;
    api.demoAccounts().then(
      () => live && setDemo(true),
      () => undefined,
    );
    return () => {
      live = false;
    };
  }, []);

  return (
    <div className="shell">
      <header className="topbar">
        <div className="topbar-brand">
          <span className="logo" aria-hidden="true" />
          <div>
            <strong>Rotadesk</strong>
            <span className="topbar-business">
              {me.business.name} · {zoneLabel(me.business.timeZone)}
            </span>
          </div>
        </div>
        <nav className="topbar-nav" aria-label="Main">
          <NavLink to="/rota">Rota</NavLink>
          <NavLink to="/swaps">Swaps</NavLink>
          <NavLink to="/availability">Availability</NavLink>
          {demo && <NavLink to="/inbox">Inbox</NavLink>}
        </nav>
        <div className="topbar-user">
          <span>
            {me.name} <span className="role">{me.role}</span>
          </span>
          <button type="button" className="link-button" onClick={() => void signOut()}>
            Sign out
          </button>
        </div>
      </header>
      <main className="content">
        <Outlet />
      </main>
    </div>
  );
}
