import { Suspense } from 'react';
import { Navigate, Route, Routes } from 'react-router-dom';
import { GuestOnly, NotFoundRoute, RequireAuth, RequirePermission } from '@/access/RouteGuards';
import { Loading } from '@/components/ui/Notice';
import AcceptInvitePage from '@/features/auth/AcceptInvitePage';
import AccountPage from '@/features/account/AccountPage';
import LoginPage from '@/features/auth/LoginPage';
import RegisterPage from '@/features/auth/RegisterPage';
import { useApp } from '@/navigation/AppContext';
import { pages } from '@/navigation/pages';

/**
 * Every address of the member app. The pages behind sign-in come from the page table: each is wrapped in a permission
 * check, then loaded on demand, so which components run depends on what the person's access token holds.
 */
export function MemberRoutes() {
  const { homePath } = useApp();

  return (
    <Routes>
      <Route path="/" element={<Navigate to={homePath} replace />} />

      {/* Open to everyone, signed in or not: an invitation is how a person gets an account, and the secret in the address is the only credential. */}
      <Route path="/accept-invite" element={<AcceptInvitePage />} />

      <Route element={<GuestOnly />}>
        <Route path="/login" element={<LoginPage />} />
        <Route path="/register" element={<RegisterPage />} />
      </Route>

      <Route element={<RequireAuth />}>
        {pages.map(({ key, path, permission, Component }) => (
          <Route
            key={key}
            path={path}
            element={
              <RequirePermission permission={permission}>
                <Suspense fallback={<Loading />}>
                  <Component />
                </Suspense>
              </RequirePermission>
            }
          />
        ))}
        {/* Behind sign-in but not in the menu, and for every member: it needs no permission, only the person themselves. */}
        <Route path="/account" element={<AccountPage />} />
      </Route>

      <Route path="*" element={<NotFoundRoute />} />
    </Routes>
  );
}
