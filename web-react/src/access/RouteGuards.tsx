import type { ReactNode } from 'react';
import { Navigate, Outlet } from 'react-router-dom';
import { AppShell } from '@/components/layout/AppShell';
import { BareNotFoundPage, BootScreen, ForbiddenPage, NotFoundPage } from '@/features/errors/StatusPages';
import { useApp } from '@/navigation/AppContext';
import { useSession } from '@/session/SessionContext';
import { Can } from './Can';
import type { PermissionRule } from './permissions';

/**
 * A layout route for the pages behind sign-in. A visitor who is not signed in is sent to the sign-in form (and Back
 * does not return to the page the guard turned away); a signed-in person gets the app shell around the page.
 */
export function RequireAuth() {
  const session = useSession();
  const { loginPath } = useApp();

  if (session.status === 'unknown') {
    return <BootScreen />;
  }
  if (session.status === 'signedOut') {
    return <Navigate to={loginPath} replace />;
  }
  return (
    <AppShell>
      <Outlet />
    </AppShell>
  );
}

/** A layout route for sign-in and sign-up: a signed-in person has no use for them and goes home. */
export function GuestOnly() {
  const session = useSession();
  const { homePath } = useApp();

  if (session.status === 'unknown') {
    return <BootScreen />;
  }
  if (session.status === 'signedIn') {
    return <Navigate to={homePath} replace />;
  }
  return <Outlet />;
}

/**
 * The page, only for a person whose token holds what the rule asks for; the "no access" page for anyone else.
 * The children are not rendered then, so a lazy page's code is not even downloaded.
 */
export function RequirePermission({ children, ...rule }: PermissionRule & { children: ReactNode }) {
  return (
    <Can {...rule} fallback={<ForbiddenPage />}>
      {children}
    </Can>
  );
}

/** An address that is no page: inside the frame for someone signed in, bare for a visitor. */
export function NotFoundRoute() {
  const session = useSession();

  if (session.status === 'unknown') {
    return <BootScreen />;
  }
  if (session.status === 'signedIn') {
    return (
      <AppShell>
        <NotFoundPage />
      </AppShell>
    );
  }
  return <BareNotFoundPage />;
}
