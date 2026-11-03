import { useRef, useState, type FormEvent } from 'react';
import { FormError } from '@/components/ui/Notice';
import { PasswordField, TextField } from '@/components/ui/fields';
import { messageForSignInError, validateSignIn } from '@/lib/messages';
import { useSessionActions } from '@/session/SessionContext';

/**
 * The email and password form, for both apps: it signs in through whichever session the page was started with (the
 * member session, or the Site admin's). Signing in changes the session, and the route guard then moves the page on.
 * `initialError` is a message to show at once, such as the one Google's return leaves behind.
 */
export function SignInForm({ initialError = null }: { initialError?: string | null }) {
  const session = useSessionActions();
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState<string | null>(initialError);
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
  );
}
