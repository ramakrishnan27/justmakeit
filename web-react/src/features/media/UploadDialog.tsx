import { useId, useRef, useState, type DragEvent } from 'react';
import type { MediaAsset, MediaConfig, MediaFolder } from '@/api/media';
import { UiIcon } from '@/components/icons/UiIcon';
import { Modal } from '@/components/ui/Modal';
import { FormError } from '@/components/ui/Notice';
import { messageForMediaError } from '@/lib/messages';
import { checkFile, formatBytes, parseTags } from '@/lib/media';
import { useMediaApi } from './hooks';

type Status = 'ready' | 'uploading' | 'done' | 'failed';

interface Entry {
  key: number;
  file: File;
  /** Why this file will not be sent, found before anything was sent. */
  problem: string | null;
  status: Status;
  /** Why the server refused it. */
  error: string | null;
}

const statusText: Record<Status, string> = { ready: 'Ready', uploading: 'Uploading…', done: 'Uploaded', failed: 'Not uploaded' };

/**
 * Adds files to the library: choose them (or drop them on the box), say which folder and which tags, and send them one after another, so
 * each file has its own answer and one that is refused does not stop the rest. A file that can be seen to be too large or of a kind that
 * is not accepted is marked at once and never sent; the server still decides about the rest. Dropping files has a tap alternative: the
 * Choose files button. The dialog cannot be closed while a file is being sent.
 */
export function UploadDialog({
  workspaceId,
  folders,
  defaultFolderId,
  config,
  onUploaded,
  onClose,
}: {
  workspaceId: string;
  folders: readonly MediaFolder[];
  defaultFolderId: string;
  config: MediaConfig;
  /** Called once after a batch in which at least one file went through, with the files that did, so the page behind can show them. */
  onUploaded: (uploaded: MediaAsset[]) => void;
  onClose: () => void;
}) {
  const media = useMediaApi();
  const titleId = useId();
  const input = useRef<HTMLInputElement>(null);
  const counter = useRef(0);
  const [entries, setEntries] = useState<Entry[]>([]);
  const [folderId, setFolderId] = useState(defaultFolderId);
  const [tagsText, setTagsText] = useState('');
  const [over, setOver] = useState(false);
  const [running, setRunning] = useState(false);
  const [message, setMessage] = useState<string | null>(null);

  const { tags, problem: tagProblem } = parseTags(tagsText);
  const sendable = entries.filter((entry) => entry.problem === null && (entry.status === 'ready' || entry.status === 'failed'));
  const finished = entries.length > 0 && entries.every((entry) => entry.status === 'done' || entry.problem !== null);

  function add(files: Iterable<File>) {
    const added = [...files].map<Entry>((file) => ({ key: (counter.current += 1), file, problem: checkFile(file, config), status: 'ready', error: null }));
    if (added.length > 0) {
      setMessage(null);
      setEntries((current) => [...current, ...added]);
    }
  }

  function onDrop(event: DragEvent<HTMLDivElement>) {
    event.preventDefault();
    setOver(false);
    if (!running) {
      add(Array.from(event.dataTransfer.files));
    }
  }

  const update = (key: number, change: Partial<Entry>) =>
    setEntries((current) => current.map((entry) => (entry.key === key ? { ...entry, ...change } : entry)));

  async function send() {
    if (running || sendable.length === 0 || tagProblem !== null) {
      return;
    }
    setRunning(true);
    setMessage(null);
    const uploaded: MediaAsset[] = [];
    for (const entry of sendable) {
      update(entry.key, { status: 'uploading', error: null });
      try {
        uploaded.push(await media.upload({ workspaceId, file: entry.file, folderId: folderId || undefined, tags }));
        update(entry.key, { status: 'done' });
      } catch (error) {
        update(entry.key, { status: 'failed', error: messageForMediaError(error) });
      }
    }
    setRunning(false);
    if (uploaded.length > 0) {
      onUploaded(uploaded);
      setMessage(uploaded.length === 1 ? '1 file was uploaded.' : `${uploaded.length} files were uploaded.`);
    }
  }

  return (
    <Modal titleId={titleId} onClose={onClose} dismissible={!running} closeDisabled={running}>
      <h2 id={titleId}>Upload Media</h2>
      <p className="muted small">
        Images, videos, PDFs and audio (PNG, JPEG, GIF, WebP, MP4, MOV, WebM, PDF, MP3, WAV), up to {formatBytes(config.maxFileBytes)} each.
      </p>

      <div className="field">
        <label htmlFor="upload-folder">Folder</label>
        <select id="upload-folder" value={folderId} disabled={running} onChange={(event) => setFolderId(event.target.value)}>
          <option value="">No folder (Uncategorized)</option>
          {folders.map((folder) => (
            <option key={folder.id} value={folder.id}>
              {folder.name}
            </option>
          ))}
        </select>
      </div>

      <div className="field">
        <label htmlFor="upload-tags">Tags</label>
        <input
          id="upload-tags"
          type="text"
          autoComplete="off"
          placeholder="summer, beach, product"
          value={tagsText}
          disabled={running}
          aria-describedby="upload-tags-hint upload-tags-error"
          aria-invalid={tagProblem !== null}
          onChange={(event) => setTagsText(event.target.value)}
        />
        <p className="hint" id="upload-tags-hint">
          Optional. Separate tags with commas; they are put on every file below.
        </p>
        <FormError id="upload-tags-error" message={tagProblem} />
      </div>

      <div
        className="media-drop"
        data-over={over ? 'true' : undefined}
        onDragOver={(event) => {
          event.preventDefault();
          setOver(true);
        }}
        onDragLeave={() => setOver(false)}
        onDrop={onDrop}
      >
        <UiIcon name="upload" />
        <p>Drag files here, or</p>
        <button className="button" type="button" disabled={running} onClick={() => input.current?.click()}>
          Choose files
        </button>
        <input
          ref={input}
          className="visually-hidden"
          type="file"
          multiple
          tabIndex={-1}
          aria-label="Choose files to upload"
          accept={config.extensions.join(',')}
          onChange={(event) => {
            add(Array.from(event.target.files ?? []));
            // So the same file can be chosen again after it was taken off the list.
            event.target.value = '';
          }}
        />
      </div>

      {entries.length > 0 ? (
        <ul className="media-queue" aria-label="Files to upload">
          {entries.map((entry) => (
            <li key={entry.key} className="media-queue-item" data-status={entry.problem ? 'problem' : entry.status}>
              <div className="media-queue-text">
                <span className="media-name" title={entry.file.name}>
                  {entry.file.name}
                </span>
                <span className="media-sub">{formatBytes(entry.file.size)}</span>
                {entry.problem || entry.error ? (
                  <span className="form-error" role="alert">
                    {entry.problem ?? entry.error}
                  </span>
                ) : (
                  <span className="media-status">{statusText[entry.status]}</span>
                )}
              </div>
              {entry.status !== 'done' && entry.status !== 'uploading' ? (
                <button
                  className="button button-icon"
                  type="button"
                  aria-label={`Remove ${entry.file.name} from the list`}
                  disabled={running}
                  onClick={() => setEntries((current) => current.filter((item) => item.key !== entry.key))}
                >
                  <UiIcon name="close" />
                </button>
              ) : null}
            </li>
          ))}
        </ul>
      ) : null}

      <div aria-live="polite">
        {message ? (
          <p className="muted" role="status">
            {message}
          </p>
        ) : null}
      </div>

      <div className="confirm-actions">
        <button className="button" type="button" disabled={running} onClick={onClose}>
          {finished ? 'Done' : 'Cancel'}
        </button>
        <button className="button button-primary" type="button" disabled={running || sendable.length === 0 || tagProblem !== null} onClick={() => void send()}>
          {running ? 'Uploading…' : sendable.length === 0 ? 'Upload' : sendable.length === 1 ? 'Upload 1 file' : `Upload ${sendable.length} files`}
        </button>
      </div>
    </Modal>
  );
}
