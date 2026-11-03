import { Suspense } from 'react';
import { Route, Routes } from 'react-router-dom';
import { GuestOnly, NotFoundRoute, RequireAuth } from '@/access/RouteGuards';
import { Loading } from '@/components/ui/Notice';
import AdminLoginPage from '@/features/admin/AdminLoginPage';
import { adminPages } from '@/navigation/adminPages';

/**
 * Every address of the Site admin app, all under /admin. The sign-in page is bare (no shell, so no menu); the pages
 * behind it come from the admin page table and are drawn inside the app shell, which is where the menu is.
 */
export function AdminRoutes() {
  return (
    <Routes>
      <Route element={<GuestOnly />}>
        <Route path="/admin/login" element={<AdminLoginPage />} />
      </Route>

      <Route element={<RequireAuth />}>
        {adminPages.map(({ key, path, Component }) => (
          <Route
            key={key}
            path={path}
            element={
              <Suspense fallback={<Loading />}>
                <Component />
              </Suspense>
            }
          />
        ))}
      </Route>

      <Route path="*" element={<NotFoundRoute />} />
    </Routes>
  );
}
