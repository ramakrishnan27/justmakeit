import { useRef, useState, type FormEvent } from 'react';
import { PageTitle } from '@/components/ui/PageTitle';
import { FormError } from '@/components/ui/Notice';
import { Section } from '@/components/ui/Section';
import { PasswordField } from '@/components/ui/fields';
import { focusField } from '@/features/auth/hooks';
import { minPasswordLength, problemForChangePasswordError, validateChangePassword } from '@/lib/messages';
import { useApi, useMember } from '@/session/SessionContext';

/**
 * The person's own account. For now that is one thing: choosing a new password. Changing it asks for the current one, ends
 * every other session of theirs (a copied sign-in on another device stops working) and keeps this one going. Nothing
 * typed here is kept: the fields are cleared when the password was changed, and nothing is stored in the page.
 */
function ChangePasswordForm({ email }: { email: string }) {
  const api = useApi();
  const form = useRef<HTMLFormElement>(null);
  const [current, setCurrent] = useState('');
  const [next, setNext] = useState('');
  const [confirm, setConfirm] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [changed, setChanged] = useState(false);
  const [submitting, setSubmitting] = useState(false);

  function fail(message: string, field: string) {
    setError(message);
    if (field) {
      focusField(form.current, field);
    }
  }

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setError(null);
    setChanged(false);

    const problem = validateChangePassword({ current, next, confirm });
    if (problem) {
      fail(problem.message, problem.field);
      return;
    }

    setSubmitting(true);
    try {
      await api.request('/v1/auth/change-password', { method: 'POST', body: { currentPassword: current, newPassword: next } });
      setCurrent('');
      setNext('');
      setConfirm('');
      setChanged(true);
    } catch (failure) {
      const reported = problemForChangePasswordError(failure);
      fail(reported.message, reported.field);
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <form ref={form} className="card account-card" noValidate onSubmit={handleSubmit}>
      {/* For a password manager: it saves the new password against this address. Not for people. */}
      <input className="visually-hidden" type="text" name="username" autoComplete="username" value={email} readOnly tabIndex={-1} aria-hidden="true" />

      <PasswordField
        id="currentPassword"
        name="currentPassword"
        label="Current password"
        autoComplete="current-password"
        maxLength={128}
        required
        aria-describedby="password-error"
        value={current}
        onChange={(event) => setCurrent(event.target.value)}
      />
      <PasswordField
        id="newPassword"
        name="newPassword"
        label="New password"
        hint={`At least ${minPasswordLength} characters, and not the one you use now.`}
        autoComplete="new-password"
        maxLength={128}
        required
        aria-describedby="password-error"
        value={next}
        onChange={(event) => setNext(event.target.value)}
      />
      <PasswordField
        id="confirmPassword"
        name="confirmPassword"
        label="Type the new password again"
        autoComplete="new-password"
        maxLength={128}
        required
        aria-describedby="password-error"
        value={confirm}
        onChange={(event) => setConfirm(event.target.value)}
      />

      <FormError id="password-error" message={error} />
      <p className="form-status" role="status">
        {changed ? 'Your password was changed. Everywhere else you were signed in, you are signed out.' : null}
      </p>
      <button className="button button-primary button-pill" type="submit" disabled={submitting}>
        Change password
      </button>
    </form>
  );
}

export default function AccountPage() {
  const { user } = useMember();

  return (
    <>
      <PageTitle documentTitle="Account">Account</PageTitle>
      <p className="muted">{`Signed in as ${user.email}.`}</p>

      <Section title="Change password">
        <ChangePasswordForm email={user.email} />
      </Section>
    </>
  );
}
