import { createContext, useContext, type ReactNode } from 'react';
import type { AppDefinition } from './apps';

const AppContext = createContext<AppDefinition | null>(null);

/** Says which app the components below belong to (the member app or the Site admin app). Made once, in main.tsx (or by a test). */
export function AppProvider({ app, children }: { app: AppDefinition; children: ReactNode }) {
  return <AppContext.Provider value={app}>{children}</AppContext.Provider>;
}

/** The app these components belong to: its addresses, its name, the pages behind its sign-in. */
export function useApp(): AppDefinition {
  const app = useContext(AppContext);
  if (!app) {
    throw new Error('This component must be inside an AppProvider.');
  }
  return app;
}
