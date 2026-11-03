import { useRef, useState, type FormEvent } from 'react';
import { Link } from 'react-router-dom';
import type { BusinessModel } from '@/api/types';
import { FormError } from '@/components/ui/Notice';
import { PasswordField, TextField } from '@/components/ui/fields';
import { googleRegisterPath } from '@/lib/google';
import { messageForGoogleError, messageForRegisterError, minPasswordLength, validateRegistration } from '@/lib/messages';
import { businessModels, organizationNameLabel } from '@/lib/roles';
import { useSessionActions } from '@/session/SessionContext';
import { AuthLayout } from './AuthLayout';
import { focusField, useProviders, useReturnedErrorCode } from './hooks';

type Method = 'google' | 'email';

function BusinessModelChoices({ value, onChange }: { value: BusinessModel | ''; onChange: (model: BusinessModel) => void }) {
  return (
    <fieldset className="field choices">
      <legend>What kind of organization is this?</legend>
      {businessModels.map((model) => (
        <label className="choice" htmlFor={`model-${model.value}`} key={model.value}>
          <input
            type="radio"
            name="businessModel"
            value={model.value}
            id={`model-${model.value}`}
            required
            checked={value === model.value}
            onChange={() => onChange(model.value)}
          />
          <span className="choice-title">{model.title}</span>
          <span className="choice-text">{model.description}</span>
        </label>
      ))}
    </fieldset>
  );
}

function MethodChoices({ value, onChange }: { value: Method | ''; onChange: (method: Method) => void }) {
  return (
    <fieldset className="field choices">
      <legend>How do you want to sign up?</legend>
      <label className="choice choice-compact" htmlFor="method-google">
        <input type="radio" name="method" value="google" id="method-google" checked={value === 'google'} onChange={() => onChange('google')} />
        <span className="choice-title">Continue with Google</span>
      </label>
      <label className="choice choice-compact" htmlFor="method-email">
        <input type="radio" name="method" value="email" id="method-email" checked={value === 'email'} onChange={() => onChange('email')} />
        <span className="choice-title">Email and password</span>
      </label>
    </fieldset>
  );
}

/**
 * The sign-up page. First the kind of organization and its name, then how to sign up: with Google, or with an
 * email and password (those fields appear only when that is chosen). Creating the account changes the session, and
 * the route guard then moves the page to the dashboard.
 */
export default function RegisterPage() {
  const session = useSessionActions();
  const providers = useProviders();
  const returnedCode = useReturnedErrorCode();
  const form = useRef<HTMLFormElement>(null);

  const [businessModel, setBusinessModel] = useState<BusinessModel | ''>('');
  const [organizationName, setOrganizationName] = useState('');
  const [picked, setPicked] = useState<Method | ''>('');
  const [fullName, setFullName] = useState('');
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState<string | null>(() => messageForGoogleError(returnedCode));
  const [submitting, setSubmitting] = useState(false);

  // Until the server answers there is no method; without Google there is only one, so no choice is shown;
  // with both on offer neither is preselected.
  const method: Method | '' = providers === null ? '' : providers.google ? picked : 'email';

  function fail(message: string, field?: string) {
    setError(message);
    if (field) {
      focusField(form.current, field);
    }
  }

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setError(null);

    const problem = validateRegistration({ businessModel, organizationName, method, fullName, email, password });
    if (problem || businessModel === '') {
      fail(problem?.message ?? 'Choose what kind of organization this is.', problem?.field ?? 'businessModel');
      return;
    }

    if (method === 'google') {
      // A plain navigation: the server sends the browser to Google and brings it back signed in.
      window.location.assign(googleRegisterPath({ businessModel, organizationName }));
      return;
    }

    setSubmitting(true);
    try {
      await session.register({
        email: email.trim(),
        password,
        fullName: fullName.trim() || null,
        organizationName: organizationName.trim(),
        businessModel,
      });
    } catch (failure) {
      fail(messageForRegisterError(failure));
      setPassword('');
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <AuthLayout
      titleId="register-title"
      title="Create your account"
      documentTitle="Create account"
      intro="Choose how you work, then how you want to sign up."
      wide
      footer={
        <>
          Already have an account? <Link to="/login">Sign in</Link>
        </>
      }
    >
      <form
        ref={form}
        noValidate
        aria-busy={submitting}
        onSubmit={handleSubmit}
        // A message about a missing choice should not outlive the person fixing it.
        onInput={() => setError(null)}
      >
        <BusinessModelChoices value={businessModel} onChange={setBusinessModel} />

        <TextField
          id="organizationName"
          name="organizationName"
          label={organizationNameLabel(businessModel)}
          type="text"
          autoComplete="organization"
          maxLength={200}
          required
          value={organizationName}
          onChange={(event) => setOrganizationName(event.target.value)}
        />

        {providers?.google ? <MethodChoices value={picked} onChange={setPicked} /> : null}

        {method === 'email' ? (
          <div id="email-fields">
            <TextField
              id="fullName"
              name="fullName"
              label="Your name (optional)"
              type="text"
              autoComplete="name"
              maxLength={200}
              value={fullName}
              onChange={(event) => setFullName(event.target.value)}
            />
            <TextField
              id="email"
              name="email"
              label="Email"
              type="email"
              autoComplete="username"
              inputMode="email"
              autoCapitalize="none"
              spellCheck={false}
              value={email}
              onChange={(event) => setEmail(event.target.value)}
            />
            <PasswordField
              id="password"
              name="password"
              label="Password"
              hint={`At least ${minPasswordLength} characters.`}
              autoComplete="new-password"
              minLength={minPasswordLength}
              maxLength={128}
              value={password}
              onChange={(event) => setPassword(event.target.value)}
            />
          </div>
        ) : null}

        <FormError id="register-error" message={error} />

        {method !== '' ? (
          <button className="button button-primary button-block" type="submit" disabled={submitting}>
            {submitting ? 'Creating account…' : method === 'google' ? 'Continue with Google' : 'Create account'}
          </button>
        ) : null}
      </form>
    </AuthLayout>
  );
}
