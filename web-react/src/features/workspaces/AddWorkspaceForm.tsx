import { useRef, useState, type FormEvent } from 'react';
import { FormError } from '@/components/ui/Notice';
import { TextField } from '@/components/ui/fields';
import { messageForWorkspaceError } from '@/lib/messages';
import { useApi } from '@/session/SessionContext';

/** Adds a workspace. The page shows it only to someone who holds workspaces.create; the server checks the permission again. */
export function AddWorkspaceForm({ word, onAdded }: { word: string; onAdded: () => void }) {
  const api = useApi();
  const [name, setName] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const input = useRef<HTMLInputElement>(null);

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setError(null);

    if (name.trim().length === 0) {
      setError(`Enter a name for the ${word}.`);
      input.current?.focus();
      return;
    }

    setSubmitting(true);
    try {
      await api.request('/v1/workspaces', { method: 'POST', body: { name: name.trim() } });
      onAdded();
    } catch (failure) {
      setError(messageForWorkspaceError(failure, word));
      setSubmitting(false);
    }
  }

  return (
    <div className="card add-workspace-card">
      <h3>{`Add a ${word}`}</h3>
      <form className="add-workspace" noValidate onSubmit={handleSubmit}>
        <TextField
          id="workspaceName"
          name="workspaceName"
          label={`New ${word} name`}
          type="text"
          maxLength={200}
          required
          aria-describedby="workspace-error"
          ref={input}
          value={name}
          onChange={(event) => setName(event.target.value)}
        />
        <FormError id="workspace-error" message={error} />
        <button className="button button-primary" type="submit" disabled={submitting}>
          {`Add ${word}`}
        </button>
      </form>
    </div>
  );
}
