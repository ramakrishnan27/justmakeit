import { useEffect, useRef, useState, type FormEvent } from 'react';
import { Link, useLocation, useNavigate } from 'react-router-dom';
import { ApiError } from '@/api/client';
import type { InvitationPreview } from '@/api/types';
import { FormError } from '@/components/ui/Notice';
import { PasswordField, TextField } from '@/components/ui/fields';
import { formatDate } from '@/lib/format';
import {
  messageForAcceptError,
  messageForInvitationPreviewError,
  minPasswordLength,
  validateAcceptance,
} from '@/lib/messages';
import { roleTitle } from '@/lib/roles';
import { useApp } from '@/navigation/AppContext';
import { useApi, useSession, useSessionActions } from '@/session/SessionContext';
import { AuthLayout } from './AuthLayout';
import { focusField } from './hooks';

// The secret of an invitation link is 43 letters, digits, "-" and "_". Anything else in the address is not one.
const tokenShape = /^[A-Za-z0-9_-]{1,200}$/;

/** The secret in the address (`/accept-invite#<secret>`), or null when the address has none that could be one. */
function readToken(hash: string): string | null {
  const text = hash.startsWith('#') ? hash.slice(1) : hash;
  return tokenShape.test(text) ? text : null;
}

type Preview =
  | { status: 'missing' }
  | { status: 'checking' }
  | { status: 'invalid'; message: string }
  | { status: 'ready'; invitation: InvitationPreview };

/**
 * Where an invitation link leads. The person sees which organization invited them and in which role, chooses a name and
 * a password, and is signed in. The secret is read from the part of the address after the #, which no server ever
 * receives, held in this component's state, and then taken out of the address bar. It is sent only in the body of a POST.
 */
export default function AcceptInvitePage() {
  const api = useApi();
  const session = useSessionActions();
  const current = useSession();
  const navigate = useNavigate();
  const location = useLocation();
  const { homePath, loginPath } = useApp();
  const form = useRef<HTMLFormElement>(null);

  // Reading is pure, so React may run it twice in development; the removal from the address is an effect.
  const [token] = useState(() => readToken(location.hash));
  const [preview, setPreview] = useState<Preview>(token === null ? { status: 'missing' } : { status: 'checking' });
  const [fullName, setFullName] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);

  useEffect(() => {
    if (location.hash !== '') {
      navigate({ pathname: location.pathname, search: location.search, hash: '' }, { replace: true });
    }
  }, [location.hash, location.pathname, location.search, navigate]);

  useEffect(() => {
    if (token === null) {
      return;
    }
    let active = true;
    api.request<InvitationPreview>('/v1/auth/invitations/preview', { method: 'POST', body: { token }, auth: false }).then(
      (invitation) => {
        if (active) {
          setPreview({ status: 'ready', invitation });
        }
      },
      (failure: unknown) => {
        if (active) {
          setPreview({ status: 'invalid', message: messageForInvitationPreviewError(failure) });
        }
      },
    );
    return () => {
      active = false;
    };
  }, [api, token]);

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setError(null);

    const problem = validateAcceptance(fullName, password);
    if (problem) {
      setError(problem.message);
      focusField(form.current, problem.field);
      return;
    }

    setSubmitting(true);
    try {
      await session.acceptInvitation({ token: token ?? '', fullName: fullName.trim(), password });
      navigate(homePath, { replace: true });
    } catch (failure) {
      if (failure instanceof ApiError && failure.status === 404) {
        setPreview({ status: 'invalid', message: messageForAcceptError(failure) });
        return;
      }
      setError(messageForAcceptError(failure));
      setSubmitting(false);
    }
  }

  const invitation = preview.status === 'ready' ? preview.invitation : null;

  return (
    <AuthLayout
      titleId="accept-title"
      title={invitation ? `Join ${invitation.organizationName}` : 'Your invitation'}
      documentTitle="Accept invitation"
      intro={
        invitation
          ? `You were invited as ${roleTitle(invitation.role)}. Choose your name and a password to finish. The invitation ends ${formatDate(invitation.expiresAt)}.`
          : 'Join your organization on Platform.'
      }
      footer={
        <>
          Already have an account? <Link to={loginPath}>Sign in</Link>
        </>
      }
    >
      {preview.status === 'checking' ? (
        <p className="muted" role="status">
          Checking your invitation…
        </p>
      ) : null}

      {preview.status === 'missing' ? (
        <p role="alert">This page needs the link from your invitation. Open the link you were sent, or ask your organization admin for a new one.</p>
      ) : null}

      {preview.status === 'invalid' ? <p role="alert">{preview.message}</p> : null}

      {invitation ? (
        <form ref={form} noValidate onSubmit={handleSubmit}>
          {current.status === 'signedIn' ? (
            <p className="muted small">Accepting signs you out of the account you are using now.</p>
          ) : null}
          <TextField
            id="acceptEmail"
            name="email"
            label="Your email address"
            type="email"
            autoComplete="username"
            readOnly
            value={invitation.email}
          />
          <TextField
            id="acceptName"
            name="fullName"
            label="Your name"
            type="text"
            autoComplete="name"
            maxLength={200}
            required
            aria-describedby="accept-error"
            value={fullName}
            onChange={(event) => setFullName(event.target.value)}
          />
          <PasswordField
            id="acceptPassword"
            name="password"
            label="Choose a password"
            hint={`At least ${minPasswordLength} characters.`}
            autoComplete="new-password"
            maxLength={128}
            required
            aria-describedby="accept-error"
            value={password}
            onChange={(event) => setPassword(event.target.value)}
          />
          <FormError id="accept-error" message={error} />
          <button className="button button-primary button-block" type="submit" disabled={submitting}>
            Create my account
          </button>
        </form>
      ) : null}
    </AuthLayout>
  );
}
