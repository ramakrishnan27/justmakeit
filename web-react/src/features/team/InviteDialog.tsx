import { useEffect, useRef, useState, type FormEvent, type RefObject } from 'react';
import type { BusinessModel, CreatedInvitation, OrganizationRole } from '@/api/types';
import { focusField } from '@/features/auth/hooks';
import { Modal } from '@/components/ui/Modal';
import { FormError } from '@/components/ui/Notice';
import { TextField } from '@/components/ui/fields';
import { formatDate } from '@/lib/format';
import { messageForInviteError, validateInvite } from '@/lib/messages';
import { responsibilities, rolesFor, roleTitle } from '@/lib/roles';
import type { TeamActions } from './useTeam';

/** The address of the page that accepts an invitation. The secret sits after the #, so no server log or Referer ever holds it. */
function inviteLink(token: string): string {
  return `${window.location.origin}/accept-invite#${token}`;
}

function InviteForm({
  model,
  invite,
  onCreated,
  onCancel,
  onBusyChange,
}: {
  model: BusinessModel;
  invite: TeamActions['invite'];
  onCreated: (created: CreatedInvitation) => void;
  onCancel: () => void;
  onBusyChange: (busy: boolean) => void;
}) {
  const form = useRef<HTMLFormElement>(null);
  const [fullName, setFullName] = useState('');
  const [email, setEmail] = useState('');
  const [role, setRole] = useState<OrganizationRole | ''>('');
  const [error, setError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);

  useEffect(() => {
    focusField(form.current, 'fullName');
  }, []);

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setError(null);

    const problem = validateInvite(fullName, email, role);
    if (problem) {
      setError(problem.message);
      focusField(form.current, problem.field);
      return;
    }

    setSubmitting(true);
    onBusyChange(true);
    try {
      onCreated(await invite(fullName.trim(), email.trim(), role as OrganizationRole));
    } catch (failure) {
      setError(messageForInviteError(failure));
      setSubmitting(false);
    } finally {
      onBusyChange(false);
    }
  }

  return (
    <form ref={form} noValidate onSubmit={handleSubmit}>
      <h2 id="invite-title">Invite an employee</h2>
      <p className="muted small">
        No email is sent: you get a link to pass on yourself, and the person chooses their own password. Or activate them
        yourself from the list, with a default password.
      </p>

      <TextField
        id="inviteName"
        name="fullName"
        label="Full name"
        type="text"
        autoComplete="off"
        maxLength={200}
        required
        aria-describedby="invite-error"
        value={fullName}
        onChange={(event) => setFullName(event.target.value)}
      />

      <TextField
        id="inviteEmail"
        name="email"
        label="Email address"
        type="email"
        autoComplete="off"
        maxLength={254}
        required
        aria-describedby="invite-error"
        value={email}
        onChange={(event) => setEmail(event.target.value)}
      />

      <fieldset className="field choices">
        <legend>Role</legend>
        {rolesFor(model).map((candidate) => (
          <label className="choice" htmlFor={`invite-role-${candidate}`} key={candidate}>
            <input
              type="radio"
              name="role"
              value={candidate}
              id={`invite-role-${candidate}`}
              checked={role === candidate}
              onChange={() => setRole(candidate)}
            />
            <span className="choice-title">{roleTitle(candidate)}</span>
            <span className="choice-text">{responsibilities(model, candidate)}</span>
          </label>
        ))}
      </fieldset>

      <FormError id="invite-error" message={error} />
      <div className="invite-actions">
        <button className="button button-primary button-pill" type="submit" disabled={submitting}>
          Create invite link
        </button>
        <button className="button button-pill" type="button" onClick={onCancel} disabled={submitting}>
          Cancel
        </button>
      </div>
    </form>
  );
}

/**
 * The link of a new invitation, shown once: the server keeps only a hash of its secret, so this is the one chance to copy
 * it. The secret lives in this component's state and nowhere else (not in storage, not in the address bar).
 */
function InviteLink({ created, onAnother, onDone }: { created: CreatedInvitation; onAnother: () => void; onDone: () => void }) {
  const heading = useRef<HTMLHeadingElement>(null);
  const field = useRef<HTMLInputElement>(null);
  const [copy, setCopy] = useState<string | null>(null);
  const { invitation } = created;
  const link = inviteLink(created.token);

  useEffect(() => {
    heading.current?.focus();
  }, []);

  async function handleCopy() {
    try {
      await navigator.clipboard.writeText(link);
      setCopy('Link copied.');
    } catch {
      // No clipboard (an insecure page, or the browser said no): select the link so the person can copy it themselves.
      field.current?.select();
      setCopy('Press Ctrl+C (or ⌘C) to copy the link.');
    }
  }

  return (
    <div>
      <h2 id="invite-ready-title" ref={heading} tabIndex={-1}>
        Invite link ready
      </h2>
      <p className="muted small">
        {`Send this link to ${invitation.fullName.length > 0 ? `${invitation.fullName} (${invitation.email})` : invitation.email}. It lets them join as ${roleTitle(invitation.role)} and works until ${formatDate(invitation.expiresAt)}. `}
        It is shown only now: if you lose it, delete the invitation and make a new one.
      </p>

      <div className="field">
        <label htmlFor="inviteLink">Invite link</label>
        <div className="input-row">
          <input id="inviteLink" ref={field} readOnly value={link} onFocus={(event) => event.currentTarget.select()} />
          <button className="button button-pill" type="button" onClick={() => void handleCopy()}>
            Copy
          </button>
        </div>
        <p className="hint" role="status">
          {copy}
        </p>
      </div>

      <div className="invite-actions">
        <button className="button button-primary button-pill" type="button" onClick={onDone}>
          Done
        </button>
        <button className="button button-pill" type="button" onClick={onAnother}>
          Invite another
        </button>
      </div>
    </div>
  );
}

/**
 * The dialog the New Employee button opens: the form, then the link it made. While the link is on screen (the server shows
 * it once) or a request is on its way, Escape and a tap outside do not close it; Done, Cancel or the × do.
 */
export function InviteDialog({
  model,
  invite,
  onClose,
  returnFocus,
}: {
  model: BusinessModel;
  invite: TeamActions['invite'];
  onClose: () => void;
  returnFocus: RefObject<HTMLElement | null>;
}) {
  const [created, setCreated] = useState<CreatedInvitation | null>(null);
  const [busy, setBusy] = useState(false);

  return (
    <Modal
      titleId={created ? 'invite-ready-title' : 'invite-title'}
      onClose={onClose}
      dismissible={created === null && !busy}
      closeDisabled={busy}
      returnFocus={returnFocus}
    >
      {created ? (
        <InviteLink created={created} onAnother={() => setCreated(null)} onDone={onClose} />
      ) : (
        <InviteForm model={model} invite={invite} onCreated={setCreated} onCancel={onClose} onBusyChange={setBusy} />
      )}
    </Modal>
  );
}
