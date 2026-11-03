import { PageTitle } from '@/components/ui/PageTitle';
import { SignInForm } from '@/features/auth/SignInForm';

/**
 * The Site admin sign-in page, at /admin/login. A bare page: no app shell, so no header and no menu, because nobody is
 * signed in yet. Signing in uses the Site admin session (its own endpoint and its own refresh cookie), and the route
 * guard then moves the page to the Site admin dashboard. The server answers a wrong password, an unknown email and an
 * account that is not a Site admin with the same message, so this page does not reveal who is one.
 */
export default function AdminLoginPage() {
  return (
    <main id="main" className="auth-page">
      <section className="card auth-card" aria-labelledby="login-title">
        <p className="brand">Platform</p>
        <PageTitle id="login-title" documentTitle="Sign in">
          Site admin sign in
        </PageTitle>
        <p className="muted">Sign in with your site administrator account.</p>
        <SignInForm />
      </section>
    </main>
  );
}
