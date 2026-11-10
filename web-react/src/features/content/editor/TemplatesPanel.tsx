import { useCallback, useEffect, useId, useState, type FormEvent } from 'react';
import type { Template } from '@/api/content';
import { Modal } from '@/components/ui/Modal';
import { contentTypeLabel } from '@/lib/content';
import { messageForContentError } from '@/lib/messages';
import { ConfirmDialog } from '@/components/ui/ConfirmDialog';
import { useContentApi } from '../hooks';
import type { PostDraft } from '../postDraft';
import { isDocEmpty } from '../richText/doc';

const SHOWN = 4;

type State = { status: 'loading' } | { status: 'error'; message: string } | { status: 'ready'; templates: Template[] };

/**
 * "Templates": the workspace's saved starting points for this kind of content. Choosing one fills the title, the text and the
 * hashtags (after asking, if something has been written). "Save as template" keeps the post being written as a new one. "View all"
 * lists every template with a button to use or delete it.
 */
export function TemplatesPanel({
  draft,
  canCreate,
  readOnly,
  onApply,
}: {
  draft: PostDraft;
  canCreate: boolean;
  readOnly: boolean;
  onApply: (template: Template) => void;
}) {
  const content = useContentApi();
  const [state, setState] = useState<State>({ status: 'loading' });
  const [attempt, setAttempt] = useState(0);
  const [pending, setPending] = useState<Template | null>(null);
  const [saving, setSaving] = useState(false);
  const [listing, setListing] = useState(false);
  const { workspaceId, contentType } = draft;

  useEffect(() => {
    let active = true;
    content.templates({ workspaceId, type: contentType }).then(
      (templates) => active && setState({ status: 'ready', templates }),
      (error: unknown) => active && setState({ status: 'error', message: messageForContentError(error) }),
    );
    return () => {
      active = false;
    };
  }, [content, workspaceId, contentType, attempt]);

  const reload = useCallback(() => setAttempt((count) => count + 1), []);
  const written = draft.title.trim().length > 0 || !isDocEmpty(draft.bodyDoc);

  function choose(template: Template) {
    if (written) {
      setPending(template);
    } else {
      onApply(template);
    }
  }

  const templates = state.status === 'ready' ? state.templates : [];

  return (
    <section className="panel" aria-labelledby="panel-templates">
      <div className="panel-head">
        <h2 id="panel-templates">Templates</h2>
        {templates.length > SHOWN ? (
          <button type="button" className="button button-quiet" onClick={() => setListing(true)}>
            View All
          </button>
        ) : null}
      </div>

      {state.status === 'loading' ? <p className="muted small" role="status">Loading templates…</p> : null}
      {state.status === 'error' ? (
        <p className="form-error" role="alert">
          {state.message}{' '}
          <button className="button button-quiet" type="button" onClick={reload}>
            Try again
          </button>
        </p>
      ) : null}
      {state.status === 'ready' && templates.length === 0 ? <p className="muted small">No {contentTypeLabel(contentType).toLowerCase()} templates yet.</p> : null}

      {templates.length > 0 ? (
        <ul className="template-grid" aria-label="Templates">
          {templates.slice(0, SHOWN).map((template) => (
            <li key={template.id}>
              <button type="button" className="template-card" disabled={readOnly} onClick={() => choose(template)}>
                <strong>{template.name}</strong>
                <span className="muted small">{template.bodyText.length > 0 ? template.bodyText : template.title}</span>
              </button>
            </li>
          ))}
        </ul>
      ) : null}

      {canCreate && !readOnly ? (
        <button type="button" className="button" disabled={!written} onClick={() => setSaving(true)}>
          Save as template
        </button>
      ) : null}

      {pending ? (
        <ConfirmDialog
          title="Use this template?"
          confirmLabel="Use template"
          busy={false}
          error={null}
          onCancel={() => setPending(null)}
          onConfirm={() => {
            onApply(pending);
            setPending(null);
          }}
        >
          <p>
            “{pending.name}” will replace the title, text and hashtags you have written so far.
          </p>
        </ConfirmDialog>
      ) : null}

      {saving ? <SaveTemplateDialog draft={draft} onClose={() => setSaving(false)} onSaved={reload} /> : null}
      {listing ? (
        <AllTemplatesDialog
          templates={templates}
          canDelete={canCreate && !readOnly}
          onClose={() => setListing(false)}
          onUse={(template) => {
            setListing(false);
            choose(template);
          }}
          onDeleted={reload}
        />
      ) : null}
    </section>
  );
}

function SaveTemplateDialog({ draft, onClose, onSaved }: { draft: PostDraft; onClose: () => void; onSaved: () => void }) {
  const content = useContentApi();
  const titleId = useId();
  const [name, setName] = useState(draft.title.trim().slice(0, 120));
  const [description, setDescription] = useState('');
  const [busy, setBusy] = useState(false);
  const [problem, setProblem] = useState<string | null>(null);

  async function submit(event: FormEvent) {
    event.preventDefault();
    if (name.trim().length === 0) {
      setProblem('Give the template a name.');
      return;
    }
    setBusy(true);
    setProblem(null);
    try {
      await content.createTemplate({
        workspaceId: draft.workspaceId,
        name: name.trim(),
        description: description.trim(),
        contentType: draft.contentType,
        title: draft.title.trim(),
        bodyDoc: isDocEmpty(draft.bodyDoc) ? null : draft.bodyDoc,
        hashtags: [...draft.hashtags],
      });
      onSaved();
      onClose();
    } catch (error) {
      setProblem(messageForContentError(error));
      setBusy(false);
    }
  }

  return (
    <Modal titleId={titleId} onClose={onClose} closeDisabled={busy} dismissible={!busy}>
      <h2 id={titleId}>Save as template</h2>
      <p className="muted">The title, text and hashtags are kept, for this workspace and this kind of content.</p>
      <form onSubmit={(event) => void submit(event)} noValidate>
        <div className="field">
          <label htmlFor="template-name">Template name</label>
          <input id="template-name" type="text" maxLength={120} value={name} onChange={(event) => setName(event.target.value)} autoFocus />
        </div>
        <div className="field">
          <label htmlFor="template-description">Description (optional)</label>
          <input id="template-description" type="text" maxLength={500} value={description} onChange={(event) => setDescription(event.target.value)} />
        </div>
        {problem ? (
          <p className="form-error" role="alert">
            {problem}
          </p>
        ) : null}
        <div className="confirm-actions">
          <button className="button" type="button" disabled={busy} onClick={onClose}>
            Cancel
          </button>
          <button className="button button-primary" type="submit" disabled={busy}>
            {busy ? 'Saving…' : 'Save template'}
          </button>
        </div>
      </form>
    </Modal>
  );
}

function AllTemplatesDialog({
  templates,
  canDelete,
  onClose,
  onUse,
  onDeleted,
}: {
  templates: readonly Template[];
  canDelete: boolean;
  onClose: () => void;
  onUse: (template: Template) => void;
  onDeleted: () => void;
}) {
  const content = useContentApi();
  const titleId = useId();
  const [busyId, setBusyId] = useState<string | null>(null);
  const [problem, setProblem] = useState<string | null>(null);

  async function remove(template: Template) {
    setBusyId(template.id);
    setProblem(null);
    try {
      await content.deleteTemplate(template.id);
      onDeleted();
    } catch (error) {
      setProblem(messageForContentError(error));
    } finally {
      setBusyId(null);
    }
  }

  return (
    <Modal titleId={titleId} onClose={onClose}>
      <h2 id={titleId}>All templates</h2>
      {problem ? (
        <p className="form-error" role="alert">
          {problem}
        </p>
      ) : null}
      <ul className="template-list">
        {templates.map((template) => (
          <li key={template.id}>
            <div>
              <strong>{template.name}</strong>
              {template.description.length > 0 ? <p className="muted small">{template.description}</p> : null}
            </div>
            <div className="template-actions">
              <button type="button" className="button" onClick={() => onUse(template)}>
                Use
              </button>
              {canDelete ? (
                <button
                  type="button"
                  className="button button-quiet"
                  aria-label={`Delete template ${template.name}`}
                  disabled={busyId === template.id}
                  onClick={() => void remove(template)}
                >
                  Delete
                </button>
              ) : null}
            </div>
          </li>
        ))}
      </ul>
    </Modal>
  );
}
