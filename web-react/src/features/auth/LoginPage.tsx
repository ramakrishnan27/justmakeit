import { useRef, useState, type FormEvent } from 'react';
import { Link } from 'react-router-dom';
import { FormError } from '@/components/ui/Notice';
import { PasswordField, TextField } from '@/components/ui/fields';
import { googleLoginPath } from '@/lib/google';
import { messageForGoogleError, messageForSignInError, validateSignIn } from '@/lib/messages';
import { useSessionActions } from '@/session/SessionContext';
import { AuthLayout } from './AuthLayout';
import { useProviders, useReturnedErrorCode } from './hooks';

/** The member sign-in page. Signing in changes the session, and the route guard then moves the page to the dashboard. */
export default function LoginPage() {
  const session = useSessionActions();
  const providers = useProviders();
  const returnedCode = useReturnedErrorCode();

  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState<string | null>(() => messageForGoogleError(returnedCode));
  const [submitting, setSubmitting] = useState(false);
  const emailInput = useRef<HTMLInputElement>(null);
  const passwordInput = useRef<HTMLInputElement>(null);

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setError(null);

    const problem = validateSignIn(email, password);
    if (problem) {
      setError(problem.message);
      (problem.field === 'email' ? emailInput : passwordInput).current?.focus();
      return;
    }

    setSubmitting(true);
    try {
      await session.signIn(email.trim(), password);
    } catch (failure) {
      setError(messageForSignInError(failure));
      setPassword('');
      passwordInput.current?.focus();
    } finally {
      setSubmitting(false);
    }
  }

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

      <form noValidate aria-busy={submitting} onSubmit={handleSubmit}>
        <TextField
          id="email"
          name="email"
          label="Email"
          type="email"
          autoComplete="username"
          inputMode="email"
          autoCapitalize="none"
          spellCheck={false}
          required
          aria-describedby="login-error"
          ref={emailInput}
          value={email}
          onChange={(event) => setEmail(event.target.value)}
        />
        <PasswordField
          id="password"
          name="password"
          label="Password"
          autoComplete="current-password"
          required
          aria-describedby="login-error"
          ref={passwordInput}
          value={password}
          onChange={(event) => setPassword(event.target.value)}
        />
        <FormError id="login-error" message={error} />
        <button className="button button-primary button-block" type="submit" disabled={submitting}>
          {submitting ? 'Signing in…' : 'Sign in'}
        </button>
      </form>
    </AuthLayout>
  );
}
