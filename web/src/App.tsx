import { Navigate, Outlet, Route, Routes, useLocation } from 'react-router';

import { Layout } from './components/Layout';
import { AvailabilityPage } from './pages/AvailabilityPage';
import { NotFoundPage } from './pages/NotFoundPage';
import { RotaPage } from './pages/RotaPage';
import { SignInPage } from './pages/SignInPage';
import { useSession } from './session';

function RequireSession() {
  const { state, refresh } = useSession();
  const location = useLocation();

  if (state.status === 'loading') {
    return <p className="page-message">Loading…</p>;
  }
  if (state.status === 'failed') {
    return (
      <div className="page-message">
        <p>{state.message}</p>
        <button type="button" onClick={() => void refresh()}>
          Try again
        </button>
      </div>
    );
  }
  if (state.status === 'signed-out') {
    // Coming back after signing in is for an expired session or a shared link, not for the next person to use
    // this browser after someone signed out.
    return <Navigate to="/sign-in" replace state={state.byChoice ? null : { from: location.pathname }} />;
  }
  return <Outlet />;
}

export function App() {
  return (
    <Routes>
      <Route path="/sign-in" element={<SignInPage />} />
      <Route element={<RequireSession />}>
        <Route element={<Layout />}>
          <Route index element={<Navigate to="/rota" replace />} />
          <Route path="rota" element={<RotaPage />} />
          <Route path="rota/:monday" element={<RotaPage />} />
          <Route path="availability" element={<AvailabilityPage />} />
          <Route path="*" element={<NotFoundPage />} />
        </Route>
      </Route>
    </Routes>
  );
}
