import { useMemo } from 'react';
import { useLocation, useParams, useSearchParams } from 'react-router-dom';
import type { Post } from '@/api/content';
import { ErrorNotice, Loading } from '@/components/ui/Notice';
import { useWorkspaces } from '@/features/workspaces/useWorkspaces';
import { isContentType } from '@/lib/content';
import { messageForContentLoadError } from '@/lib/messages';
import { workspaceWords } from '@/lib/roles';
import { useMember } from '@/session/SessionContext';
import { PostEditor } from './PostEditor';
import { useContentMeta } from './hooks';
import { draftFromPost, newDraft } from './postDraft';
import { usePostSource } from './usePostSource';

/**
 * Everything the editor needs before it can draw: the post (when editing), what is real here and the platforms' limits, and the
 * organization's workspaces. Each can fail on its own and says which. Once they are all here the editor starts from them; it is
 * started afresh (a new `key`) for a different post.
 */
export default function PostEditorPage({ mode }: { mode: 'new' | 'edit' }) {
  const { organization } = useMember();
  const { id } = useParams();
  const location = useLocation();
  const [search] = useSearchParams();
  const seed = (location.state as { post?: Post } | null)?.post ?? null;

  const source = usePostSource(mode === 'edit' ? id : undefined, seed);
  const { state: meta, reload: reloadMeta } = useContentMeta();
  const { state: workspaceState, reload: reloadWorkspaces } = useWorkspaces();

  const workspaces = workspaceState.status === 'ready' ? workspaceState.workspaces : [];
  const wanted = search.get('workspace');
  const wantedType = search.get('type');

  const initialDraft = useMemo(() => {
    if (mode === 'edit') {
      return source.state.status === 'ready' ? draftFromPost(source.state.post) : null;
    }
    const workspaceId = workspaces.find((workspace) => workspace.id === wanted)?.id ?? workspaces[0]?.id;
    return workspaceId ? newDraft({ contentType: isContentType(wantedType) ? wantedType : 'social_post', workspaceId }) : null;
  }, [mode, source.state, workspaces, wanted, wantedType]);

  if (mode === 'edit' && source.state.status === 'error') {
    return (
      <ErrorNotice message={messageForContentLoadError(source.state.error, 'The post')}>
        <button className="button" type="button" onClick={source.reload}>
          Try again
        </button>
      </ErrorNotice>
    );
  }
  if (meta.status === 'error') {
    return (
      <ErrorNotice message={messageForContentLoadError(meta.error, 'Content Studio')}>
        <button className="button" type="button" onClick={reloadMeta}>
          Try again
        </button>
      </ErrorNotice>
    );
  }
  if (workspaceState.status === 'error') {
    return (
      <ErrorNotice message="Your workspaces could not be loaded.">
        <button className="button" type="button" onClick={reloadWorkspaces}>
          Try again
        </button>
      </ErrorNotice>
    );
  }
  if (workspaceState.status === 'ready' && workspaces.length === 0) {
    return <ErrorNotice message="Your organization has no workspace to put a post in yet." />;
  }
  if (meta.status === 'loading' || initialDraft === null) {
    return <Loading text="Loading the editor…" />;
  }

  const post = mode === 'edit' && source.state.status === 'ready' ? source.state.post : null;
  const version = mode === 'edit' && source.state.status === 'ready' ? source.state.version : 0;

  return (
    <PostEditor
      key={`${post?.id ?? 'new'}:${version}`}
      post={post}
      initialDraft={initialDraft}
      rules={meta.rules}
      capabilities={meta.capabilities}
      workspaces={workspaces}
      workspaceWord={workspaceWords(organization.businessModel).one}
      accountName={organization.name}
      onReload={source.reload}
    />
  );
}
