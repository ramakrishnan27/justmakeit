import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { Link, useLocation, useNavigate } from 'react-router-dom';
import { ApiError } from '@/api/client';
import type { Capabilities, ContentType, PlatformRule, Post, Template } from '@/api/content';
import type { Workspace } from '@/api/types';
import { useCan } from '@/access/hooks';
import { PERMISSIONS } from '@/access/permissions';
import { Icon } from '@/components/icons/Icon';
import { Badge } from '@/components/ui/Badge';
import { Modal } from '@/components/ui/Modal';
import { PageTitle } from '@/components/ui/PageTitle';
import { statusLabel, statusTone } from '@/lib/content';
import { formatDate } from '@/lib/format';
import { contentFieldProblems, messageForContentError } from '@/lib/messages';
import { ConfirmDialog } from '@/components/ui/ConfirmDialog';
import { ChipInput } from './editor/ChipInput';
import { CustomizeStep } from './editor/CustomizeStep';
import { DeliveryStatus } from './editor/DeliveryStatus';
import { LivePreview } from './editor/LivePreview';
import { MoreOptions } from './editor/MoreOptions';
import { MediaPanel } from './editor/MediaPanel';
import { AiPanel, ContentTypePicker } from './editor/Panels';
import { PostSettings } from './editor/PostSettings';
import { ReviewStep } from './editor/ReviewStep';
import { Stepper, type Step } from './editor/Stepper';
import { TemplatesPanel } from './editor/TemplatesPanel';
import { useDebounced } from '@/lib/uiHooks';
import { useContentApi } from './hooks';
import { browserTimeZone } from './localTime';
import {
  draftFromPost,
  draftsEqual,
  hasContent,
  toSaveBody,
  type DraftMedia,
  type PostDraft,
} from './postDraft';
import { docFromText, withHashtag } from './richText/doc';
import { RichTextEditor, type RichTextEditorHandle } from './richText/RichTextEditor';
import { isEditable } from './rowActionRules';
import { useLeaveGuard } from './useLeaveGuard';

type Busy = 'save' | 'publish' | 'act' | null;

/**
 * The Create / Edit page: the post being written, in three steps (write it, customize it for each platform, schedule or publish it).
 * It holds the draft in the page and sends it only when asked: Save Draft, or Schedule / Publish (which saves first). An edit carries
 * the version it started from, so a save over someone else's change is refused and offered a reload instead of overwriting. A post
 * that can no longer be edited, or a person who may not edit, gets the same page read only.
 */
export function PostEditor({
  post: initialPost,
  initialDraft,
  rules,
  capabilities,
  workspaces,
  workspaceWord,
  accountName,
  onReload,
}: {
  post: Post | null;
  initialDraft: PostDraft;
  rules: readonly PlatformRule[];
  capabilities: Capabilities;
  workspaces: readonly Pick<Workspace, 'id' | 'name'>[];
  workspaceWord: string;
  accountName: string;
  /** Reads the post again (after a conflict) and starts the page from the latest version. */
  onReload: () => void;
}) {
  const content = useContentApi();
  const navigate = useNavigate();
  const location = useLocation();
  const canCreate = useCan({ permission: PERMISSIONS.contentStudioCreate });
  const canPublish = useCan({ permission: PERMISSIONS.contentStudioPublish });

  const [post, setPost] = useState<Post | null>(initialPost);
  const [draft, setDraft] = useState<PostDraft>(initialDraft);
  const [saved, setSaved] = useState<PostDraft>(initialDraft);
  const [step, setStep] = useState<Step>('create');
  const [busy, setBusy] = useState<Busy>(null);
  const [problem, setProblem] = useState<string | null>(null);
  const [fieldProblems, setFieldProblems] = useState<Record<string, string[]>>({});
  const [conflict, setConflict] = useState(false);
  const [notice, setNotice] = useState<string | null>((location.state as { notice?: string } | null)?.notice ?? null);
  const [preview, setPreview] = useState(false);
  const editor = useRef<RichTextEditorHandle>(null);

  const status = post?.status ?? 'draft';
  const readOnly = !canCreate || !isEditable(status);
  const dirty = !draftsEqual(draft, saved);
  const guard = useLeaveGuard(dirty);

  const update = useCallback((change: Partial<PostDraft>) => setDraft((current) => ({ ...current, ...change })), []);
  const setMedia = useCallback(
    (change: (current: readonly DraftMedia[]) => readonly DraftMedia[]) => setDraft((current) => ({ ...current, media: change(current.media) })),
    [],
  );

  // --- Suggestions for hashtags and tags --------------------------------------------------------------------------------
  const [hashtagText, setHashtagText] = useState('');
  const debouncedHashtag = useDebounced(hashtagText, 250);
  const [hashtagSuggestions, setHashtagSuggestions] = useState<string[]>([]);
  const [tagSuggestions, setTagSuggestions] = useState<string[]>([]);
  const { workspaceId } = draft;

  useEffect(() => {
    let active = true;
    content.hashtagSuggestions({ workspaceId, q: debouncedHashtag, limit: 8 }).then(
      (found) => active && setHashtagSuggestions(found.map((entry) => entry.hashtag)),
      () => active && setHashtagSuggestions([]),
    );
    return () => {
      active = false;
    };
  }, [content, workspaceId, debouncedHashtag]);

  useEffect(() => {
    let active = true;
    content.tags(workspaceId).then(
      (found) => active && setTagSuggestions(found.map((tag) => tag.name)),
      () => active && setTagSuggestions([]),
    );
    return () => {
      active = false;
    };
  }, [content, workspaceId]);

  // --- Doing things ----------------------------------------------------------------------------------------------------

  function report(error: unknown) {
    if (error instanceof ApiError && (error.code === 'content_changed' || error.code === 'post_publishing')) {
      setConflict(true);
    }
    setProblem(messageForContentError(error));
    setFieldProblems(contentFieldProblems(error));
  }

  async function perform<T>(kind: Exclude<Busy, null>, work: () => Promise<T>): Promise<T | null> {
    setBusy(kind);
    setProblem(null);
    setFieldProblems({});
    setConflict(false);
    setNotice(null);
    try {
      return await work();
    } catch (error) {
      report(error);
      return null;
    } finally {
      setBusy(null);
    }
  }

  /** Writes the draft: a new post, or a new version of this one. The page then holds what the server holds. */
  async function persist(): Promise<Post> {
    const body = toSaveBody(draft, { timezone: browserTimeZone(), expectedUpdatedAt: post?.updatedAt });
    const result = post ? await content.update(post.id, body) : await content.create(body);
    const canonical = draftFromPost(result);
    setPost(result);
    setSaved(canonical);
    setDraft(canonical);
    return result;
  }

  async function saveDraft() {
    const wasNew = post === null;
    const result = await perform('save', persist);
    if (!result) {
      return;
    }
    if (wasNew) {
      // From now on the address names the post, so a reload or a copied link finds it.
      navigate(`/content-studio/${result.id}`, { replace: true, state: { post: result, notice: 'Draft saved.' } });
    } else {
      setNotice('Draft saved.');
    }
  }

  async function publish(work: (saved: Post) => Promise<Post>, done: (result: Post) => string) {
    const result = await perform('publish', async () => work(await persist()));
    if (result) {
      navigate('/content-studio', { state: { notice: done(result) } });
    }
  }

  const label = (result: Post) => (result.title.length > 0 ? `“${result.title}”` : 'The post');

  function schedule(at: string) {
    void publish((saved) => content.schedule(saved.id, at), (result) => `${label(result)} is scheduled for ${formatDate(at, { withTime: true })}.`);
  }

  function publishNow() {
    void publish((saved) => content.publishNow(saved.id), (result) => `${label(result)} is being published.`);
  }

  async function act(work: () => Promise<unknown>, done: string, to: 'list' | 'here' = 'list') {
    const ok = await perform('act', async () => {
      await work();
      return true;
    });
    if (ok) {
      if (to === 'list') {
        navigate('/content-studio', { state: { notice: done } });
      } else {
        onReload();
      }
    }
  }

  function applyTemplate(template: Template) {
    update({
      title: template.title.length > 0 ? template.title : draft.title,
      bodyDoc: template.bodyDoc ?? docFromText(template.bodyText),
      hashtags: template.hashtags,
    });
    setNotice(`“${template.name}” was applied. Save the draft to keep it.`);
  }

  // --- Problems the server named, for the summary --------------------------------------------------------------------------
  const serverProblems = useMemo(() => Object.values(fieldProblems).flat(), [fieldProblems]);
  const platformProblem = fieldProblems.platforms?.[0] ?? null;

  const heading = post ? 'Edit Post' : 'Create New Post';
  const showWorkspaceChoice = workspaces.length > 1;
  const dateText = post?.scheduledAt ? formatDate(post.scheduledAt, { withTime: true }) : null;

  return (
    <div className="editor-page studio-wide">
      <header className="studio-header">
        <span className="studio-mark" aria-hidden="true">
          <Icon name="edit" />
        </span>
        <div className="studio-heading">
          <PageTitle documentTitle={heading}>{heading}</PageTitle>
          <p className="muted">Turn your ideas into amazing content</p>
        </div>
        <Link className="button" to="/content-studio">
          ← Back to Content Studio
        </Link>
      </header>

      <Stepper step={step} onStep={setStep} />

      <div aria-live="polite">
        {notice ? (
          <div className="notice notice-success" role="status">
            <p>{notice}</p>
            <button className="button button-quiet" type="button" onClick={() => setNotice(null)}>
              Dismiss
            </button>
          </div>
        ) : null}
        {problem ? (
          <div className="notice notice-error" role="alert">
            <p>{problem}</p>
            {serverProblems.length > 0 && !conflict ? (
              <ul>
                {serverProblems.map((message) => (
                  <li key={message}>{message}</li>
                ))}
              </ul>
            ) : null}
            {conflict ? (
              <button className="button" type="button" onClick={onReload}>
                Reload the latest version
              </button>
            ) : null}
          </div>
        ) : null}
      </div>

      {post ? (
        <div className="status-line">
          <Badge tone={statusTone(post.status)}>{statusLabel(post.status)}</Badge>
          {post.status === 'scheduled' && dateText ? <span className="muted">Goes out {dateText}</span> : null}
          {post.status === 'scheduled' && canPublish ? (
            <button className="button" type="button" disabled={busy !== null} onClick={() => void act(() => content.unschedule(post.id), 'The post was taken back to a draft.', 'here')}>
              Unschedule
            </button>
          ) : null}
        </div>
      ) : null}

      {readOnly ? (
        <div className="notice" role="status">
          <p>
            {!canCreate
              ? 'Your role can look at this post but not change it.'
              : `A post that is ${statusLabel(status).toLowerCase()} can no longer be edited.${canCreate ? ' Duplicate it to make changes.' : ''}`}
          </p>
          <div className="notice-actions">
            {canCreate && post ? (
              <button
                className="button"
                type="button"
                disabled={busy !== null}
                onClick={() =>
                  void perform('act', async () => {
                    const copy = await content.duplicate(post.id);
                    navigate(`/content-studio/${copy.id}`, { state: { post: copy, notice: 'A copy was made as a draft.' } });
                    return copy;
                  })
                }
              >
                Duplicate
              </button>
            ) : null}
            {canPublish && post && status === 'published' ? (
              <button className="button" type="button" disabled={busy !== null} onClick={() => void act(() => content.archive(post.id), 'The post was archived.')}>
                Archive
              </button>
            ) : null}
            {canPublish && post && status === 'archived' ? (
              <button className="button" type="button" disabled={busy !== null} onClick={() => void act(() => content.restore(post.id), 'The post was restored.')}>
                Restore
              </button>
            ) : null}
          </div>
        </div>
      ) : null}

      <fieldset className="editor-fields" disabled={readOnly}>
        <legend className="visually-hidden">The post</legend>

        {step === 'create' ? (
          <div className="studio-grid">
            <div className="studio-col col-left">
              <div className="studio-item" data-slot="type">
                <ContentTypePicker value={draft.contentType} onChange={(type: ContentType) => update({ contentType: type })} disabled={readOnly} />
              </div>
              <div className="studio-item" data-slot="media">
                <MediaPanel
                  workspaceId={draft.workspaceId}
                  media={draft.media}
                  setMedia={setMedia}
                  available={capabilities.media}
                  problem={fieldProblems.media?.[0] ?? null}
                />
              </div>
              <div className="studio-item" data-slot="ai">
                <AiPanel available={capabilities.ai} />
              </div>
              <div className="studio-item" data-slot="templates">
                <TemplatesPanel draft={draft} canCreate={canCreate} readOnly={readOnly} onApply={applyTemplate} />
              </div>
            </div>

            <div className="studio-col col-center">
            <div className="studio-item" data-slot="editor">
              <section className="panel" aria-labelledby="editor-label">
                <h2 id="editor-label">Post Editor</h2>
                <div className="field">
                  <label htmlFor="post-title">Title</label>
                  <input
                    id="post-title"
                    className="title-input"
                    type="text"
                    maxLength={300}
                    placeholder="A title (needed for a blog post or a script)"
                    value={draft.title}
                    onChange={(event) => update({ title: event.target.value })}
                    aria-describedby={fieldProblems.title ? 'title-problem' : undefined}
                  />
                  {fieldProblems.title ? (
                    <p className="form-error" id="title-problem" role="alert">
                      {fieldProblems.title[0]}
                    </p>
                  ) : null}
                </div>

                <RichTextEditor value={draft.bodyDoc} onChange={(bodyDoc) => update({ bodyDoc })} disabled={readOnly} labelledBy="editor-label" handleRef={editor} />
                {fieldProblems.bodyDoc ? (
                  <p className="form-error" role="alert">
                    {fieldProblems.bodyDoc[0]}
                  </p>
                ) : null}

                <ChipInput
                  label="Hashtags"
                  prefix="#"
                  values={draft.hashtags}
                  splitOnSpace
                  disabled={readOnly}
                  placeholder="Type a hashtag and press Enter"
                  suggestions={hashtagSuggestions}
                  onType={setHashtagText}
                  onAdd={(word) => setDraft((current) => ({ ...current, hashtags: withHashtag(current.hashtags, word) }))}
                  onRemove={(value) => setDraft((current) => ({ ...current, hashtags: current.hashtags.filter((tag) => tag !== value) }))}
                  problem={fieldProblems.hashtags?.[0] ?? null}
                />
                <ChipInput
                  label="Tags"
                  values={draft.tags}
                  disabled={readOnly}
                  placeholder="Add a tag, such as Motivation"
                  suggestions={tagSuggestions}
                  hint="Tags are for finding posts in the list; they are not posted."
                  onAdd={(word) =>
                    setDraft((current) =>
                      current.tags.some((tag) => tag.toLowerCase() === word.toLowerCase()) || current.tags.length >= 20 ? current : { ...current, tags: [...current.tags, word] },
                    )
                  }
                  onRemove={(value) => setDraft((current) => ({ ...current, tags: current.tags.filter((tag) => tag !== value) }))}
                  problem={fieldProblems.tags?.[0] ?? null}
                />
              </section>
            </div>

            <div className="studio-item" data-slot="more">
              <MoreOptions draft={draft} update={update} onEmoji={() => editor.current?.toggleEmoji()} />
              {['poll', 'ctas', 'mentions', 'location'].map((key) =>
                fieldProblems[key] ? (
                  <p className="form-error" role="alert" key={key}>
                    {fieldProblems[key]?.[0]}
                  </p>
                ) : null,
              )}
            </div>
            </div>

            <div className="studio-col col-right">
            <div className="studio-item" data-slot="preview">
              <LivePreview draft={draft} accountName={accountName} />
            </div>

            <div className="studio-item" data-slot="settings">
              <PostSettings
                draft={draft}
                update={update}
                rules={rules}
                workspaces={workspaces}
                workspaceWord={workspaceWord}
                workspaceLocked={post !== null || !showWorkspaceChoice}
                campaignsAvailable={capabilities.campaigns}
                problem={platformProblem}
              />
            </div>
            </div>
          </div>
        ) : null}

        {step === 'customize' ? <CustomizeStep draft={draft} update={update} rules={rules} onBack={() => setStep('create')} /> : null}

        {step === 'publish' ? (
          <ReviewStep
            draft={draft}
            update={update}
            rules={rules}
            deliveries={post?.platforms ?? []}
            canPublish={canPublish && canCreate}
            busy={busy !== null}
            onStep={setStep}
            onSaveDraft={() => void saveDraft()}
            onSchedule={schedule}
            onPublishNow={publishNow}
          />
        ) : null}
      </fieldset>

      {step !== 'publish' && post && readOnly ? <DeliveryStatus targets={post.platforms} /> : null}

      <div className="action-bar" role="group" aria-label="Post actions">
        <button className="button" type="button" disabled={readOnly || busy !== null || !hasContent(draft)} onClick={() => void saveDraft()}>
          {busy === 'save' ? 'Saving…' : 'Save Draft'}
        </button>
        <button className="button" type="button" onClick={() => setPreview(true)}>
          Preview Post
        </button>
        {step !== 'publish' && !readOnly ? (
          <button className="button button-primary" type="button" onClick={() => setStep('publish')}>
            {canPublish ? 'Schedule & Publish' : 'Review'} →
          </button>
        ) : null}
      </div>

      {preview ? (
        <Modal titleId="preview-title" onClose={() => setPreview(false)}>
          <h2 id="preview-title" className="visually-hidden">
            Preview of the post
          </h2>
          <LivePreview draft={draft} accountName={accountName} framed={false} />
        </Modal>
      ) : null}

      {guard.target ? (
        <ConfirmDialog title="Leave without saving?" confirmLabel="Leave" cancelLabel="Keep editing" danger busy={false} error={null} onConfirm={guard.leave} onCancel={guard.stay}>
          <p>You have changes that are not saved. If you leave now they will be lost.</p>
        </ConfirmDialog>
      ) : null}
    </div>
  );
}
