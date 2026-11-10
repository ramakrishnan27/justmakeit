import { useId, useState, type FormEvent } from 'react';
import type { MediaFolder } from '@/api/media';
import { Modal } from '@/components/ui/Modal';
import { FormError } from '@/components/ui/Notice';
import { mediaFieldProblems, messageForMediaError } from '@/lib/messages';
import { useMediaApi } from './hooks';

/**
 * Makes a folder in the workspace. The name is checked here (1 to 100 characters) before it is sent, and the server's answer is shown under
 * the field it is about: a name the workspace already has, or a name it refused. The dialog cannot be closed while the request is on its way.
 */
export function CreateFolderDialog({
  workspaceId,
  onCreated,
  onClose,
}: {
  workspaceId: string;
  onCreated: (folder: MediaFolder) => void;
  onClose: () => void;
}) {
  const media = useMediaApi();
  const titleId = useId();
  const [name, setName] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const trimmed = name.trim();
    if (trimmed.length === 0 || trimmed.length > 100) {
      setError('A folder name is 1 to 100 characters.');
      return;
    }

    setError(null);
    setSubmitting(true);
    try {
      onCreated(await media.createFolder(workspaceId, trimmed));
    } catch (failure) {
      setError(mediaFieldProblems(failure).name?.[0] ?? messageForMediaError(failure));
      setSubmitting(false);
    }
  }

  return (
    <Modal titleId={titleId} onClose={onClose} dismissible={!submitting} closeDisabled={submitting}>
      <form noValidate onSubmit={(event) => void submit(event)}>
        <h2 id={titleId}>Create Folder</h2>
        <p className="muted small">Folders keep this workspace's files in order. A file can be in more than one.</p>

        <div className="field">
          <label htmlFor="folder-name">Folder name</label>
          <input
            id="folder-name"
            name="name"
            type="text"
            autoComplete="off"
            autoFocus
            maxLength={100}
            required
            aria-describedby="folder-error"
            aria-invalid={error !== null}
            value={name}
            onChange={(event) => setName(event.target.value)}
          />
        </div>

        <FormError id="folder-error" message={error} />
        <div className="confirm-actions">
          <button className="button" type="button" disabled={submitting} onClick={onClose}>
            Cancel
          </button>
          <button className="button button-primary" type="submit" disabled={submitting}>
            {submitting ? 'Creating…' : 'Create folder'}
          </button>
        </div>
      </form>
    </Modal>
  );
}
