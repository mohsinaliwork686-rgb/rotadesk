import { createContext, useCallback, useContext, useEffect, useMemo, useState } from 'react';
import type { ReactNode } from 'react';

import { ApiError, api, describeError, onSessionEnded } from './lib/api';
import type { Me } from './lib/api';

export type SessionState =
  | { status: 'loading' }
  | { status: 'signed-out'; byChoice?: boolean }
  | { status: 'failed'; message: string }
  | { status: 'signed-in'; me: Me };

interface Session {
  state: SessionState;
  /** Asks the API who is signed in, after signing in or when the page loads. */
  refresh: () => Promise<void>;
  signOut: () => Promise<void>;
}

const SessionContext = createContext<Session | null>(null);

async function whoIsSignedIn(): Promise<SessionState> {
  try {
    return { status: 'signed-in', me: await api.me() };
  } catch (error) {
    return error instanceof ApiError && error.status === 401
      ? { status: 'signed-out' }
      : { status: 'failed', message: describeError(error) };
  }
}

export function SessionProvider({ children }: { children: ReactNode }) {
  const [state, setState] = useState<SessionState>({ status: 'loading' });

  const refresh = useCallback(async () => {
    setState(await whoIsSignedIn());
  }, []);

  const signOut = useCallback(async () => {
    try {
      await api.signOut();
    } catch {
      // Show the sign-in page anyway; an old cookie is refused by the API once it expires.
    }
    setState({ status: 'signed-out', byChoice: true });
  }, []);

  useEffect(() => {
    let live = true;
    void whoIsSignedIn().then((found) => live && setState(found));
    return () => {
      live = false;
    };
  }, []);

  // Any request the API answers with 401 means the cookie has gone, so show the sign-in page.
  useEffect(() => onSessionEnded(() => setState({ status: 'signed-out' })), []);

  const session = useMemo(() => ({ state, refresh, signOut }), [state, refresh, signOut]);
  return <SessionContext.Provider value={session}>{children}</SessionContext.Provider>;
}

export function useSession(): Session {
  const session = useContext(SessionContext);
  if (!session) {
    throw new Error('useSession needs a SessionProvider above it.');
  }
  return session;
}

/** The signed-in person. Only for pages behind RequireSession. */
export function useMe(): Me {
  const { state } = useSession();
  if (state.status !== 'signed-in') {
    throw new Error('useMe needs someone signed in.');
  }
  return state.me;
}

export const isManager = (me: Me) => me.role !== 'Staff';
