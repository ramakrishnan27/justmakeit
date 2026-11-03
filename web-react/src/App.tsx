import { Suspense } from 'react';
import { Navigate, Route, Routes } from 'react-router-dom';
import { GuestOnly, NotFoundRoute, RequireAuth, RequirePermission } from '@/access/RouteGuards';
import { Loading } from '@/components/ui/Notice';
import LoginPage from '@/features/auth/LoginPage';
import RegisterPage from '@/features/auth/RegisterPage';
import { homePath, pages } from '@/navigation/pages';

/**
 * Every address of the member app. The pages behind sign-in come from the page table: each is wrapped in a permission
 * check, then loaded on demand, so which components run depends on what the person's access token holds.
 */
export function App() {
  return (
    <Routes>
      <Route path="/" element={<Navigate to={homePath} replace />} />

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
      </Route>

      <Route path="*" element={<NotFoundRoute />} />
    </Routes>
  );
}
