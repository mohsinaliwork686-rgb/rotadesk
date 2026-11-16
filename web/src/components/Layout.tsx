import { NavLink, Outlet } from 'react-router';

import { useMe, useSession } from '../session';
import { zoneLabel } from '../lib/time';

export function Layout() {
  const me = useMe();
  const { signOut } = useSession();

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
          <NavLink to="/availability">Availability</NavLink>
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
