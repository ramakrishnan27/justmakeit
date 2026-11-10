import { useId, useState } from 'react';
import type { MediaFolder, OrganizeMode } from '@/api/media';
import { Modal } from '@/components/ui/Modal';
import { countLabel } from '@/lib/format';

/**
 * Chooses the folder for a Move or a Copy. A move can also take the files out of every folder (Uncategorized); a copy needs a folder, since
 * putting a file "nowhere" besides where it already is means nothing. The page does the work and says here how it went.
 */
export function FolderPickerDialog({
  mode,
  count,
  folders,
  busy,
  error,
  onChoose,
  onClose,
}: {
  mode: OrganizeMode;
  count: number;
  folders: readonly MediaFolder[];
  busy: boolean;
  error: string | null;
  onChoose: (folderId: string | null) => void;
  onClose: () => void;
}) {
  const titleId = useId();
  const withNone = mode === 'move';
  const [choice, setChoice] = useState<string>(folders[0]?.id ?? '');
  const empty = !withNone && folders.length === 0;
  const verb = mode === 'move' ? 'Move' : 'Copy';

  return (
    <Modal titleId={titleId} onClose={onClose} dismissible={!busy} closeDisabled={busy}>
      <h2 id={titleId}>{`${verb} ${countLabel(count, 'file')}`}</h2>
      <p className="muted small">
        {mode === 'move' ? 'Choose where they go. They will leave the folders they are in now.' : 'Choose a folder to add them to. They stay in the folders they are in now.'}
      </p>

      {empty ? (
        <p>There are no folders yet. Create one first.</p>
      ) : (
        <fieldset className="field">
          <legend>Folder</legend>
          {withNone ? (
            <label className="choice">
              <input type="radio" name="target-folder" value="" checked={choice === ''} disabled={busy} onChange={() => setChoice('')} />
              <span>No folder (Uncategorized)</span>
            </label>
          ) : null}
          {folders.map((folder) => (
            <label key={folder.id} className="choice">
              <input type="radio" name="target-folder" value={folder.id} checked={choice === folder.id} disabled={busy} onChange={() => setChoice(folder.id)} />
              <span>{folder.name}</span>
            </label>
          ))}
        </fieldset>
      )}

      {error ? (
        <p className="form-error" role="alert">
          {error}
        </p>
      ) : null}
      <div className="confirm-actions">
        <button className="button" type="button" disabled={busy} onClick={onClose}>
          Cancel
        </button>
        <button className="button button-primary" type="button" disabled={busy || empty} onClick={() => onChoose(choice === '' ? null : choice)}>
          {busy ? 'Working…' : `${verb} here`}
        </button>
      </div>
    </Modal>
  );
}
