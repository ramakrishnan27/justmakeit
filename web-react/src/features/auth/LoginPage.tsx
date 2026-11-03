import { Link } from 'react-router-dom';
import { googleLoginPath } from '@/lib/google';
import { messageForGoogleError } from '@/lib/messages';
import { AuthLayout } from './AuthLayout';
import { useProviders, useReturnedErrorCode } from './hooks';
import { SignInForm } from './SignInForm';

/** The member sign-in page. Signing in changes the session, and the route guard then moves the page to the dashboard. */
export default function LoginPage() {
  const providers = useProviders();
  const returnedCode = useReturnedErrorCode();

  return (
    <AuthLayout
      titleId="login-title"
      title="Welcome back"
      documentTitle="Sign in"
      intro="Sign in to your organization."
      footer={
        <>
          New here? <Link to="/register">Create an account</Link>
        </>
      }
    >
      {/* Added only when the server has Google set up, so the button never leads to a dead end. */}
      <div className="google-region">
        {providers?.google ? (
          <>
            <a className="button button-google button-block" href={googleLoginPath}>
              Continue with Google
            </a>
            <p className="divider">
              <span>or sign in with email</span>
            </p>
          </>
        ) : null}
      </div>

      <SignInForm initialError={messageForGoogleError(returnedCode)} />
    </AuthLayout>
  );
}
