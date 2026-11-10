import { useCallback, useEffect, useMemo, useState } from 'react';
import { useLocation, useNavigate, useSearchParams } from 'react-router-dom';
import type { PostListItem } from '@/api/content';
import { useCan } from '@/access/hooks';
import { PERMISSIONS } from '@/access/permissions';
import { Icon } from '@/components/icons/Icon';
import { ErrorNotice, Loading } from '@/components/ui/Notice';
import { PageTitle } from '@/components/ui/PageTitle';
import { Pager } from '@/components/ui/Pager';
import { workspaceWords } from '@/lib/roles';
import { messageForContentError, messageForContentLoadError } from '@/lib/messages';
import { useMember } from '@/session/SessionContext';
import { useWorkspaces } from '@/features/workspaces/useWorkspaces';
import { ConfirmDialog } from '@/components/ui/ConfirmDialog';
import { ContentFilters } from './ContentFilters';
import { ContentTable } from './ContentTable';
import { ContentTabs } from './ContentTabs';
import { CreateMenu } from './CreateMenu';
import { useDebounced } from '@/lib/uiHooks';
import { useContentApi, useContentMeta } from './hooks';
import { filtersFromSearch, isNarrowed, noFilters, searchFromFilters, type ListFilters } from './listFilters';
import type { RowAction } from './rowActionRules';
import { useContentList } from './useContentList';

type Pending = { action: 'delete'; post: PostListItem } | null;

/**
 * Content Studio's landing page: every post of the organization as a list you can narrow by kind, status, platform, date and words,
 * with what can be done to each. Opened only by someone whose access token carries contentstudio.view; the buttons that change
 * things are drawn only for those who hold contentstudio.create or contentstudio.publish, and the server checks each request again.
 */
export default function ContentStudioPage() {
  const { organization } = useMember();
  const words = workspaceWords(organization.businessModel);
  const content = useContentApi();
  const navigate = useNavigate();
  const location = useLocation();
  const [params, setParams] = useSearchParams();
  const filters = useMemo(() => filtersFromSearch(params), [params]);

  const canCreate = useCan({ permission: PERMISSIONS.contentStudioCreate });
  const canPublish = useCan({ permission: PERMISSIONS.contentStudioPublish });
  const abilities = useMemo(() => ({ canCreate, canPublish }), [canCreate, canPublish]);

  const { state, reload } = useContentList(filters);
  const { state: meta } = useContentMeta();
  const { state: workspaceState } = useWorkspaces();
  const workspaces = workspaceState.status === 'ready' ? workspaceState.workspaces : [];

  // Where the address and the filters meet: every change is a new address, so Back goes back through what was chosen.
  const change = useCallback(
    (partial: Partial<ListFilters>, replace = false) =>
      setParams(searchFromFilters({ ...filtersFromSearch(params), ...partial }), { replace }),
    [params, setParams],
  );

  // The search box is typed into at its own pace; the address follows once the typing pauses.
  const [search, setSearch] = useState(filters.q);
  const debounced = useDebounced(search, 300);
  useEffect(() => setSearch(filters.q), [filters.q]);
  useEffect(() => {
    if (debounced.trim() !== filters.q.trim()) {
      change({ q: debounced.trim(), page: 1 }, true);
    }
    // Only when the typing settles: `change` and the filters are read then, not watched.
  }, [debounced]);

  const [busyId, setBusyId] = useState<string | null>(null);
  const [problem, setProblem] = useState<string | null>(null);
  const [pending, setPending] = useState<Pending>(null);
  const [confirmError, setConfirmError] = useState<string | null>(null);
  const notice = (location.state as { notice?: string } | null)?.notice ?? null;

  const page = state.status === 'ready' ? state.page : state.status === 'loading' ? state.previous : null;
  const counts = page?.typeCounts ?? null;

  function dismissNotice() {
    navigate({ pathname: location.pathname, search: location.search }, { replace: true, state: null });
  }

  async function run(post: PostListItem, work: () => Promise<unknown>, done: string) {
    setBusyId(post.id);
    setProblem(null);
    try {
      await work();
      navigate({ pathname: location.pathname, search: location.search }, { replace: true, state: { notice: done } });
      reload();
    } catch (error) {
      setProblem(messageForContentError(error));
    } finally {
      setBusyId(null);
    }
  }

  function onAction(action: Exclude<RowAction, 'edit' | 'open'>, post: PostListItem) {
    const name = post.title.length > 0 ? `“${post.title}”` : 'The post';
    if (action === 'delete') {
      setConfirmError(null);
      setPending({ action: 'delete', post });
    } else if (action === 'duplicate') {
      setBusyId(post.id);
      setProblem(null);
      content.duplicate(post.id).then(
        (copy) => navigate(`/content-studio/${copy.id}`, { state: { notice: 'A copy was made as a draft.' } }),
        (error: unknown) => {
          setProblem(messageForContentError(error));
          setBusyId(null);
        },
      );
    } else if (action === 'archive') {
      void run(post, () => content.archive(post.id), `${name} was archived.`);
    } else if (action === 'restore') {
      void run(post, () => content.restore(post.id), `${name} was restored.`);
    } else {
      void run(post, () => content.unschedule(post.id), `${name} was taken back to a draft.`);
    }
  }

  async function confirmDelete() {
    if (!pending) {
      return;
    }
    const { post } = pending;
    setBusyId(post.id);
    setConfirmError(null);
    try {
      await content.remove(post.id);
      setPending(null);
      navigate({ pathname: location.pathname, search: location.search }, { replace: true, state: { notice: 'The post was deleted.' } });
      reload();
    } catch (error) {
      setConfirmError(messageForContentError(error));
    } finally {
      setBusyId(null);
    }
  }

  const rules = meta.status === 'ready' ? meta.rules : [];
  const campaignsAvailable = meta.status === 'ready' && meta.capabilities.campaigns;
  const narrowed = isNarrowed(filters);

  return (
    <div className="studio-wide">
      <header className="studio-header">
        <span className="studio-mark" aria-hidden="true">
          <Icon name="edit" />
        </span>
        <div className="studio-heading">
          <PageTitle documentTitle="Content Studio">Content Studio</PageTitle>
          <p className="muted">Manage all your content in one place — create, organize and track performance.</p>
        </div>
        {canCreate ? <CreateMenu workspaceId={filters.workspaceId || undefined} /> : null}
      </header>

      <div aria-live="polite">
        {notice ? (
          <div className="notice notice-success" role="status">
            <p>{notice}</p>
            <button className="button button-quiet" type="button" onClick={dismissNotice}>
              Dismiss
            </button>
          </div>
        ) : null}
        {problem ? <ErrorNotice message={problem} /> : null}
      </div>

      <ContentTabs filters={filters} counts={counts} />

      <ContentFilters
        filters={filters}
        search={search}
        onSearch={setSearch}
        onChange={(partial) => change(partial)}
        onReset={() => {
          setSearch('');
          change({ ...noFilters, type: filters.type });
        }}
        rules={rules}
        campaignsAvailable={campaignsAvailable}
        workspaces={workspaces}
        workspaceAll={organization.businessModel === 'creator' ? 'All workspaces' : `All ${words.many.toLowerCase()}`}
      />

      <div aria-live="polite" aria-busy={state.status === 'loading'}>
        {state.status === 'error' ? (
          <ErrorNotice message={messageForContentLoadError(state.error)}>
            <button className="button" type="button" onClick={reload}>
              Try again
            </button>
          </ErrorNotice>
        ) : null}

        {state.status === 'loading' && !page ? <Loading text="Loading content…" /> : null}

        {page && page.items.length === 0 && state.status !== 'error' ? (
          <div className="notice content-empty">
            {narrowed || filters.type !== '' ? (
              <>
                <p>No content matches what you chose.</p>
                {narrowed ? (
                  <button
                    className="button"
                    type="button"
                    onClick={() => {
                      setSearch('');
                      change({ ...noFilters, type: filters.type });
                    }}
                  >
                    Reset filters
                  </button>
                ) : null}
              </>
            ) : (
              <>
                <p>There is no content yet.</p>
                {canCreate ? <p className="muted">Use “Create New Content” to write your first post.</p> : null}
              </>
            )}
          </div>
        ) : null}

        {page && page.items.length > 0 && state.status !== 'error' ? (
          <div className={`content-list${state.status === 'loading' ? ' is-refreshing' : ''}`}>
            <ContentTable items={page.items} abilities={abilities} busyId={busyId} onAction={onAction} />
            <Pager
              label="Content pages"
              page={page.page}
              pageSize={page.pageSize}
              totalCount={page.totalCount}
              totalPages={page.totalPages}
              onPage={(next) => change({ page: next })}
            />
          </div>
        ) : null}
      </div>

      {pending ? (
        <ConfirmDialog
          title="Delete this post?"
          confirmLabel="Delete"
          danger
          busy={busyId === pending.post.id}
          error={confirmError}
          onConfirm={() => void confirmDelete()}
          onCancel={() => setPending(null)}
        >
          <p>
            {pending.post.title.length > 0 ? <strong>{pending.post.title}</strong> : 'This post'} will be deleted for good, with its hashtags,
            platforms and history. This cannot be undone.
          </p>
        </ConfirmDialog>
      ) : null}
    </div>
  );
}
