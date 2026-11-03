import { useApp } from '@/navigation/AppContext';
import { AdminRoutes } from '@/routes/AdminRoutes';
import { MemberRoutes } from '@/routes/MemberRoutes';

/** The routes of the app the page was started as: the Site admin app under /admin, the member app everywhere else. */
export function App() {
  return useApp().kind === 'admin' ? <AdminRoutes /> : <MemberRoutes />;
}
