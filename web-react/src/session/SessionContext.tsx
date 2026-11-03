import { createContext, useContext, useSyncExternalStore, type ReactNode } from 'react';
import type { ApiClient } from '@/api/client';
import type { AdminState, MemberState, Session, SessionState, SignedInState } from './session';

interface AppServices {
  api: ApiClient;
  session: Session;
}

const ServicesContext = createContext<AppServices | null>(null);

/** Gives the components below it the API client and the session. Made once, in main.tsx (or by a test). */
export function SessionProvider({ api, session, children }: AppServices & { children: ReactNode }) {
  return <ServicesContext.Provider value={{ api, session }}>{children}</ServicesContext.Provider>;
}

function useServices(): AppServices {
  const services = useContext(ServicesContext);
  if (!services) {
    throw new Error('This component must be inside a SessionProvider.');
  }
  return services;
}

/** The API client. Pages call the API through it, so the access token and the single refresh stay in one place. */
export function useApi(): ApiClient {
  return useServices().api;
}

/** What the session can do: sign in, sign out, read the claims. The object never changes, so it is safe in effects. */
export function useSessionActions(): Session {
  return useServices().session;
}

/** The session as it is now; the component renders again whenever it changes. */
export function useSession(): SessionState {
  const { session } = useServices();
  return useSyncExternalStore(session.subscribe, session.getState);
}

/** The signed-in session, of either app. Only for components below RequireAuth, where being signed in is already settled. */
export function useSignedIn(): SignedInState {
  const state = useSession();
  if (state.status !== 'signedIn') {
    throw new Error('useSignedIn was used while nobody is signed in.');
  }
  return state;
}

/** The signed-in member (a role inside an organization). For the pages of the member app. */
export function useMember(): MemberState {
  const state = useSignedIn();
  if (state.kind !== 'member') {
    throw new Error('useMember was used outside the member app.');
  }
  return state;
}

/** The signed-in Site admin. For the pages of the Site admin app. */
export function useSiteAdmin(): AdminState {
  const state = useSignedIn();
  if (state.kind !== 'admin') {
    throw new Error('useSiteAdmin was used outside the Site admin app.');
  }
  return state;
}
