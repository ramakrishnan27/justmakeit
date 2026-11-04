import { useEffect, useRef, useState, type FormEvent, type RefObject } from 'react';
import { focusField } from '@/features/auth/hooks';
import { Modal } from '@/components/ui/Modal';
import { FormError } from '@/components/ui/Notice';
import { PasswordField } from '@/components/ui/fields';
import { messageForTeamActionError, minPasswordLength, validateDefaultPassword } from '@/lib/messages';
import { displayName, type TeamRow } from './teamRows';
import type { TeamActions } from './useTeam';

/**
 * Activates a waiting invitation by hand: the admin types a default password, and the person can sign in with their
 * address and that password at once. The invitation's link stops working. The password is held in this dialog's state
 * only, sent in the body of one request, and forgotten when the dialog closes.
 */
export function ActivateDialog({
  row,
  activate,
  onActivated,
  onClose,
  returnFocus,
}: {
  row: TeamRow;
  activate: TeamActions['activate'];
  onActivated: (row: TeamRow) => void;
  onClose: () => void;
  returnFocus: RefObject<HTMLElement | null>;
}) {
  const form = useRef<HTMLFormElement>(null);
  const [password, setPassword] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const name = displayName(row);

  useEffect(() => {
    focusField(form.current, 'password');
  }, []);

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setError(null);

    const problem = validateDefaultPassword(password);
    if (problem) {
      setError(problem.message);
      focusField(form.current, problem.field);
      return;
    }

    setSubmitting(true);
    try {
      await activate(row.id, password);
      onActivated(row);
    } catch (failure) {
      setError(messageForTeamActionError(failure));
      setSubmitting(false);
    }
  }

  return (
    <Modal titleId="activate-title" onClose={onClose} dismissible={!submitting} closeDisabled={submitting} returnFocus={returnFocus}>
      <form ref={form} noValidate onSubmit={handleSubmit}>
        <h2 id="activate-title">{`Activate ${name}`}</h2>
        <p className="muted small">
          {`Give ${name} a default password. They can sign in as ${row.email} with it straight away, and the invitation link stops working.`}
        </p>

        <PasswordField
          id="activatePassword"
          name="password"
          label="Default password"
          hint={`At least ${minPasswordLength} characters.`}
          autoComplete="new-password"
          maxLength={128}
          required
          aria-describedby="activate-error"
          value={password}
          onChange={(event) => setPassword(event.target.value)}
        />

        <p className="muted small">
          You will know this password, so tell it to {name} privately. There is no screen yet where they can change it.
        </p>

        <FormError id="activate-error" message={error} />
        <div className="invite-actions">
          <button className="button button-primary button-pill" type="submit" disabled={submitting}>
            Activate
          </button>
          <button className="button button-pill" type="button" onClick={onClose} disabled={submitting}>
            Cancel
          </button>
        </div>
      </form>
    </Modal>
  );
}
