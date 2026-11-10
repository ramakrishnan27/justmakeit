import { useEffect, useRef, useState, type ChangeEvent, type FormEvent, type RefObject } from 'react';
import type { SiteChannel } from '@/api/types';
import { focusField } from '@/features/auth/hooks';
import { Modal } from '@/components/ui/Modal';
import { FormError } from '@/components/ui/Notice';
import { TextField } from '@/components/ui/fields';
import {
  channelDescriptionMax,
  channelIconAccept,
  channelIconMaxBytes,
  channelNameMax,
  messageForChannelError,
  validateChannelForm,
  validateChannelIcon,
  type ChannelFormField,
} from '@/lib/channelForm';

type Problems = Partial<Record<ChannelFormField, string>>;

/** Reads a chosen file as a data: address the preview can show (the page's policy allows data: images, not blob: ones). */
function readAsDataUrl(file: File): Promise<string> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () => (typeof reader.result === 'string' ? resolve(reader.result) : reject(new Error('Not a data address.')));
    reader.onerror = () => reject(reader.error ?? new Error('The file could not be read.'));
    reader.readAsDataURL(file);
  });
}

function ChannelForm({
  create,
  onCreated,
  onCancel,
  onBusyChange,
}: {
  create: (form: FormData) => Promise<SiteChannel>;
  onCreated: (channel: SiteChannel) => void;
  onCancel: () => void;
  onBusyChange: (busy: boolean) => void;
}) {
  const form = useRef<HTMLFormElement>(null);
  const fileInput = useRef<HTMLInputElement>(null);
  // Which read of a file is the latest, so a slow read of a file the person has since replaced is ignored.
  const reads = useRef(0);
  const [name, setName] = useState('');
  const [description, setDescription] = useState('');
  const [icon, setIcon] = useState<File | null>(null);
  const [preview, setPreview] = useState<string | null>(null);
  const [problems, setProblems] = useState<Problems>({});
  const [submitting, setSubmitting] = useState(false);

  useEffect(() => {
    focusField(form.current, 'name');
  }, []);

  // A message about a field no longer holds once the person has changed that field (or anything the server said about the whole form).
  function edited(field: 'name' | 'description') {
    setProblems((current) => (current[field] || current.form ? { ...current, [field]: undefined, form: undefined } : current));
  }

  function clearIcon() {
    reads.current += 1;
    setIcon(null);
    setPreview(null);
    if (fileInput.current) {
      fileInput.current.value = '';
    }
  }

  async function handleIconChange(event: ChangeEvent<HTMLInputElement>) {
    const file = event.target.files?.[0] ?? null;
    if (!file) {
      clearIcon();
      setProblems((current) => ({ ...current, icon: undefined }));
      return;
    }

    const problem = validateChannelIcon(file);
    if (problem) {
      clearIcon();
      setProblems((current) => ({ ...current, icon: problem.message }));
      return;
    }

    const read = ++reads.current;
    setProblems((current) => ({ ...current, icon: undefined, form: undefined }));
    setIcon(file);
    try {
      const url = await readAsDataUrl(file);
      if (read === reads.current) {
        setPreview(url);
      }
    } catch {
      // No preview is not a problem: the file itself is fine, and the server checks it again.
      if (read === reads.current) {
        setPreview(null);
      }
    }
  }

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setProblems({});

    const problem = validateChannelForm(name, description, icon);
    if (problem) {
      setProblems({ [problem.field]: problem.message });
      focusField(form.current, problem.field);
      return;
    }

    const body = new FormData();
    body.set('name', name.trim());
    body.set('description', description.trim());
    if (icon) {
      body.set('icon', icon, icon.name);
    }

    setSubmitting(true);
    onBusyChange(true);
    try {
      onCreated(await create(body));
    } catch (failure) {
      const refused = messageForChannelError(failure);
      setProblems({ [refused.field]: refused.message });
      if (refused.field !== 'form') {
        focusField(form.current, refused.field);
      }
      setSubmitting(false);
    } finally {
      onBusyChange(false);
    }
  }

  return (
    <form ref={form} noValidate onSubmit={handleSubmit}>
      <h2 id="channel-title">New channel</h2>
      <p className="muted small">
        A channel is somewhere the platform sends to. Once it is added, turn it on for organizations from Users, Organizations.
      </p>

      <TextField
        id="channelName"
        name="name"
        label="Name"
        type="text"
        autoComplete="off"
        maxLength={channelNameMax}
        required
        aria-invalid={problems.name ? true : undefined}
        aria-describedby="channel-name-error"
        value={name}
        onChange={(event) => {
          setName(event.target.value);
          edited('name');
        }}
      />
      <FormError id="channel-name-error" message={problems.name ?? null} />

      <TextField
        id="channelDescription"
        name="description"
        label="Description (optional)"
        type="text"
        autoComplete="off"
        maxLength={channelDescriptionMax}
        aria-invalid={problems.description ? true : undefined}
        aria-describedby="channel-description-error"
        value={description}
        onChange={(event) => {
          setDescription(event.target.value);
          edited('description');
        }}
      />
      <FormError id="channel-description-error" message={problems.description ?? null} />

      <div className="field">
        <label htmlFor="channelIcon">Icon (optional)</label>
        <input
          id="channelIcon"
          ref={fileInput}
          name="icon"
          type="file"
          accept={channelIconAccept}
          aria-invalid={problems.icon ? true : undefined}
          aria-describedby="channel-icon-hint channel-icon-error"
          onChange={(event) => void handleIconChange(event)}
        />
        <p className="hint" id="channel-icon-hint">
          A PNG, JPEG or WebP picture of at most {channelIconMaxBytes / 1024} KB. Without one the channel gets a generic icon.
        </p>
        {icon ? (
          <div className="icon-chosen">
            {preview ? <img className="icon-preview" src={preview} alt="Preview of the chosen icon" width={48} height={48} /> : null}
            <span className="icon-chosen-name">{icon.name}</span>
            <button className="button button-pill" type="button" onClick={clearIcon} disabled={submitting}>
              Remove
            </button>
          </div>
        ) : null}
      </div>
      <FormError id="channel-icon-error" message={problems.icon ?? null} />

      <FormError id="channel-form-error" message={problems.form ?? null} />
      <div className="invite-actions">
        <button className="button button-primary button-pill" type="submit" disabled={submitting}>
          Add channel
        </button>
        <button className="button button-pill" type="button" onClick={onCancel} disabled={submitting}>
          Cancel
        </button>
      </div>
    </form>
  );
}

/**
 * The dialog the New channel button opens. While a request is on its way Escape, a tap outside and the × do nothing (the
 * answer would be lost); otherwise any of them, or Cancel, closes it. Closing it throws away what was typed.
 */
export function NewChannelDialog({
  create,
  onCreated,
  onClose,
  returnFocus,
}: {
  create: (form: FormData) => Promise<SiteChannel>;
  /** Called with the channel the server made; the page closes the dialog. */
  onCreated: (channel: SiteChannel) => void;
  onClose: () => void;
  returnFocus: RefObject<HTMLElement | null>;
}) {
  const [busy, setBusy] = useState(false);

  return (
    <Modal titleId="channel-title" onClose={onClose} dismissible={!busy} closeDisabled={busy} returnFocus={returnFocus}>
      <ChannelForm create={create} onCreated={onCreated} onCancel={onClose} onBusyChange={setBusy} />
    </Modal>
  );
}
