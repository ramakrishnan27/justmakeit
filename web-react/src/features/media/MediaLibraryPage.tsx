import { useCallback, useEffect, useMemo, useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import type { MediaAsset, MediaFolder, OrganizeMode } from '@/api/media';
import { useCan } from '@/access/hooks';
import { PERMISSIONS } from '@/access/permissions';
import { Icon } from '@/components/icons/Icon';
import { UiIcon } from '@/components/icons/UiIcon';
import { ConfirmDialog } from '@/components/ui/ConfirmDialog';
import { ErrorNotice, Loading } from '@/components/ui/Notice';
import { PageTitle } from '@/components/ui/PageTitle';
import { Pager } from '@/components/ui/Pager';
import { countLabel } from '@/lib/format';
import { messageForMediaError, messageForMediaLoadError } from '@/lib/messages';
import { workspaceWords } from '@/lib/roles';
import { useDebounced } from '@/lib/uiHooks';
import { useMember } from '@/session/SessionContext';
import { useWorkspaces } from '@/features/workspaces/useWorkspaces';
import type { AssetAction } from './AssetMenu';
import { AssetDialog } from './AssetDialog';
import { CreateFolderDialog } from './CreateFolderDialog';
import { FolderPanel } from './FolderPanel';
import { FolderPickerDialog } from './FolderPickerDialog';
import { useMediaApi, useMediaConfig, useMediaList, useMediaOverview } from './hooks';
import { filtersFromSearch, isNarrowed, noFilters, searchFromFilters, UNCATEGORIZED, type MediaFilters, type MediaView } from './mediaFilters';
import { MediaFiltersBar } from './MediaFiltersBar';
import { MediaGrid, MediaRows } from './MediaItems';
import { MediaTabs } from './MediaTabs';
import { SelectionBar } from './SelectionBar';
import { UploadDialog } from './UploadDialog';
import { UploadMenu } from './UploadMenu';

type Dialog =
  | { kind: 'upload' }
  | { kind: 'folder' }
  | { kind: 'asset'; asset: MediaAsset }
  | { kind: 'pick'; mode: OrganizeMode; ids: string[] }
  | { kind: 'delete'; assets: MediaAsset[] }
  | { kind: 'deleteFolder'; folder: MediaFolder };

const capitalize = (word: string) => word.charAt(0).toUpperCase() + word.slice(1);

/**
 * The Media Library: the files of one workspace as a grid or a list, narrowed by kind, folder, tag, size, date and words, with what can be
 * done to them. Opened only by someone whose access token carries medialibrary.view. Uploading, making folders, moving and copying need
 * medialibrary.create and deleting needs medialibrary.delete; each button is drawn only for those who hold the permission, and the server
 * checks every request again. The choices are in the address, so a reload and the back button keep them.
 */
export default function MediaLibraryPage() {
  const { organization } = useMember();
  const words = workspaceWords(organization.businessModel);
  const media = useMediaApi();
  const [params, setParams] = useSearchParams();
  const filters = useMemo(() => filtersFromSearch(params), [params]);

  const canCreate = useCan({ permission: PERMISSIONS.mediaLibraryCreate });
  const canDelete = useCan({ permission: PERMISSIONS.mediaLibraryDelete });
  const abilities = useMemo(() => ({ canCreate, canDelete }), [canCreate, canDelete]);

  // The library shows one workspace at a time: the one in the address if the organization has it, otherwise its first.
  const { state: workspaceState, reload: reloadWorkspaces } = useWorkspaces();
  const workspaces = workspaceState.status === 'ready' ? workspaceState.workspaces : [];
  const workspaceId = workspaces.find((workspace) => workspace.id === filters.workspaceId)?.id ?? workspaces[0]?.id ?? '';

  // Where the address and the choices meet: every change is a new address, so Back goes back through what was chosen.
  const change = useCallback(
    (partial: Partial<MediaFilters>, replace = false) => setParams(searchFromFilters({ ...filtersFromSearch(params), ...partial }), { replace }),
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

  const list = useMediaList(filters, workspaceId);
  const overview = useMediaOverview(workspaceId);
  const config = useMediaConfig();
  const reload = () => {
    list.reload();
    overview.reload();
  };

  const page = list.state.status === 'ready' ? list.state.data : list.state.status === 'loading' ? list.state.previous : null;
  const items = page?.items ?? [];
  const folders = (overview.state.status === 'ready' ? overview.state.data : overview.state.status === 'loading' ? overview.state.previous : null)?.folders ?? [];
  const tags = (overview.state.status === 'ready' ? overview.state.data : overview.state.status === 'loading' ? overview.state.previous : null)?.tags ?? [];

  // What is ticked belongs to the page that is showing. A new page, a new filter or a new workspace starts again.
  const [selection, setSelection] = useState<ReadonlySet<string>>(new Set());
  const listKey = `${workspaceId}|${searchFromFilters({ ...filters, view: 'grid', sort: 'newest' }).toString()}`;
  useEffect(() => setSelection(new Set()), [listKey]);
  const selected = items.filter((asset) => selection.has(asset.id));

  const [dialog, setDialog] = useState<Dialog | null>(null);
  const [busy, setBusy] = useState(false);
  const [dialogError, setDialogError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [problem, setProblem] = useState<string | null>(null);

  const closeDialog = () => {
    setDialog(null);
    setDialogError(null);
  };
  const toggle = (id: string) =>
    setSelection((current) => {
      const next = new Set(current);
      if (!next.delete(id)) {
        next.add(id);
      }
      return next;
    });
  const toggleAll = () => setSelection(selected.length === items.length ? new Set() : new Set(items.map((asset) => asset.id)));

  function onAction(action: AssetAction, asset: MediaAsset) {
    setDialogError(null);
    if (action === 'open') {
      setDialog({ kind: 'asset', asset });
    } else if (action === 'delete') {
      setDialog({ kind: 'delete', assets: [asset] });
    } else {
      setDialog({ kind: 'pick', mode: action, ids: [asset.id] });
    }
  }

  async function organize(mode: OrganizeMode, ids: string[], folderId: string | null) {
    setBusy(true);
    setDialogError(null);
    try {
      await media.organize(ids, folderId, mode);
      const where = folderId === null ? 'Uncategorized' : (folders.find((folder) => folder.id === folderId)?.name ?? 'the folder');
      setNotice(`${mode === 'move' ? 'Moved' : 'Copied'} ${countLabel(ids.length, 'file')} to ${where}.`);
      setSelection(new Set());
      closeDialog();
      reload();
    } catch (error) {
      setDialogError(messageForMediaError(error));
    } finally {
      setBusy(false);
    }
  }

  // One after another: each file has its own answer, and one that is in use must not stop the others.
  async function removeAssets(assets: MediaAsset[]) {
    setBusy(true);
    setDialogError(null);
    let deleted = 0;
    let lastError: unknown = null;
    for (const asset of assets) {
      try {
        await media.remove(asset.id);
        deleted += 1;
      } catch (error) {
        lastError = error;
      }
    }
    setBusy(false);
    if (deleted > 0) {
      setNotice(`Deleted ${countLabel(deleted, 'file')}.`);
      setSelection(new Set());
      reload();
    }
    if (lastError === null) {
      closeDialog();
    } else if (deleted > 0) {
      closeDialog();
      setProblem(`${countLabel(assets.length - deleted, 'file')} could not be deleted. ${messageForMediaError(lastError)}`);
    } else {
      setDialogError(messageForMediaError(lastError));
    }
  }

  async function removeFolder(folder: MediaFolder) {
    setBusy(true);
    setDialogError(null);
    try {
      await media.removeFolder(folder.id);
      setNotice(`Deleted the folder “${folder.name}”. Its files are still in the library.`);
      closeDialog();
      if (filters.folder === folder.id) {
        change({ folder: '', page: 1 });
      }
      reload();
    } catch (error) {
      setDialogError(messageForMediaError(error));
    } finally {
      setBusy(false);
    }
  }

  function openUpload() {
    setNotice(null);
    if (config.state.status === 'ready') {
      setDialog({ kind: 'upload' });
    } else {
      setProblem('The upload settings could not be loaded. Reload the page and try again.');
    }
  }

  const narrowed = isNarrowed(filters);
  const resetFilters = () => {
    setSearch('');
    change({ tagId: '', size: '', range: '', q: '', page: 1 });
  };
  const defaultFolder = filters.folder !== '' && filters.folder !== UNCATEGORIZED ? filters.folder : '';
  const view: MediaView = filters.view;
  const itemProps = { items, selection, selectable: canCreate || canDelete, abilities, onToggle: toggle, onAction };

  return (
    <div className="studio-wide media-page">
      <header className="studio-header">
        <span className="studio-mark" aria-hidden="true">
          <Icon name="image" />
        </span>
        <div className="studio-heading">
          <PageTitle documentTitle="Media Library">Media Library</PageTitle>
          <p className="muted">Manage and organize your images, videos and other media assets.</p>
        </div>
        {canCreate && workspaceId !== '' ? (
          <div className="media-actions">
            <button className="button" type="button" onClick={() => setDialog({ kind: 'folder' })}>
              <UiIcon name="folderPlus" />
              Create Folder
            </button>
            <UploadMenu onUploadFiles={openUpload} />
          </div>
        ) : null}
      </header>

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
          <ErrorNotice message={problem}>
            <button className="button button-quiet" type="button" onClick={() => setProblem(null)}>
              Dismiss
            </button>
          </ErrorNotice>
        ) : null}
      </div>

      {workspaceState.status === 'loading' ? <Loading text="Loading…" /> : null}
      {workspaceState.status === 'error' ? (
        <ErrorNotice message={messageForMediaLoadError(workspaceState.error, `Your ${words.one === 'workspace' ? 'workspaces' : words.many.toLowerCase()}`)}>
          <button className="button" type="button" onClick={reloadWorkspaces}>
            Try again
          </button>
        </ErrorNotice>
      ) : null}
      {workspaceState.status === 'ready' && workspaces.length === 0 ? (
        <div className="notice">
          <p>There is no {words.one} yet, so there is nowhere to keep files.</p>
        </div>
      ) : null}

      {workspaceId !== '' ? (
        <>
          {workspaces.length > 1 ? (
            <div className="filter media-workspace">
              <label htmlFor="media-workspace">{capitalize(words.one)}</label>
              <select
                id="media-workspace"
                value={workspaceId}
                onChange={(event) => setParams(searchFromFilters({ ...noFilters, view: filters.view, workspaceId: event.target.value }))}
              >
                {workspaces.map((workspace) => (
                  <option key={workspace.id} value={workspace.id}>
                    {workspace.name}
                  </option>
                ))}
              </select>
            </div>
          ) : null}

          <MediaTabs filters={filters} counts={page?.typeCounts ?? null} />

          <MediaFiltersBar filters={filters} search={search} onSearch={setSearch} onChange={(partial) => change(partial)} onReset={resetFilters} tags={tags} />

          <div className="media-layout">
            <FolderPanel
              state={overview.state}
              filters={filters}
              canCreate={canCreate}
              canDelete={canDelete}
              onCreateFolder={() => setDialog({ kind: 'folder' })}
              onDeleteFolder={(folder) => {
                setDialogError(null);
                setDialog({ kind: 'deleteFolder', folder });
              }}
              onRetry={overview.reload}
            />

            <section className="media-main" aria-label="Files">
              <SelectionBar
                selectable={canCreate || canDelete}
                count={selected.length}
                total={items.length}
                canCreate={canCreate}
                canDelete={canDelete}
                busy={busy}
                view={view}
                sort={filters.sort}
                onToggleAll={toggleAll}
                onMove={() => setDialog({ kind: 'pick', mode: 'move', ids: selected.map((asset) => asset.id) })}
                onCopy={() => setDialog({ kind: 'pick', mode: 'copy', ids: selected.map((asset) => asset.id) })}
                onDelete={() => setDialog({ kind: 'delete', assets: selected })}
                onView={(next) => change({ view: next }, true)}
                onSort={(sort) => change({ sort, page: 1 })}
              />

              <div aria-live="polite" aria-busy={list.state.status === 'loading'}>
                {list.state.status === 'error' ? (
                  <ErrorNotice message={messageForMediaLoadError(list.state.error)}>
                    <button className="button" type="button" onClick={list.reload}>
                      Try again
                    </button>
                  </ErrorNotice>
                ) : null}

                {list.state.status === 'loading' && !page ? <Loading text="Loading files…" /> : null}

                {page && items.length === 0 && list.state.status !== 'error' ? (
                  <div className="notice content-empty">
                    {narrowed || filters.type !== '' || filters.folder !== '' ? (
                      <>
                        <p>No files match what you chose.</p>
                        {narrowed ? (
                          <button className="button" type="button" onClick={resetFilters}>
                            Reset filters
                          </button>
                        ) : null}
                      </>
                    ) : (
                      <>
                        <p>There are no files yet.</p>
                        {canCreate ? <p className="muted">Use “Upload Media” to add your first file.</p> : null}
                      </>
                    )}
                  </div>
                ) : null}

                {page && items.length > 0 && list.state.status !== 'error' ? (
                  <div className={`media-list${list.state.status === 'loading' ? ' is-refreshing' : ''}`}>
                    {view === 'grid' ? <MediaGrid {...itemProps} /> : <MediaRows {...itemProps} />}
                    <Pager
                      label="Media pages"
                      page={page.page}
                      pageSize={page.pageSize}
                      totalCount={page.totalCount}
                      totalPages={page.totalPages}
                      onPage={(next) => change({ page: next })}
                    />
                  </div>
                ) : null}
              </div>
            </section>
          </div>
        </>
      ) : null}

      {dialog?.kind === 'upload' && config.state.status === 'ready' ? (
        <UploadDialog
          workspaceId={workspaceId}
          folders={folders}
          defaultFolderId={defaultFolder}
          config={config.state.data}
          onUploaded={reload}
          onClose={closeDialog}
        />
      ) : null}

      {dialog?.kind === 'folder' ? (
        <CreateFolderDialog
          workspaceId={workspaceId}
          onClose={closeDialog}
          onCreated={(folder) => {
            setNotice(`Created the folder “${folder.name}”.`);
            closeDialog();
            overview.reload();
          }}
        />
      ) : null}

      {dialog?.kind === 'asset' ? (
        <AssetDialog
          asset={dialog.asset}
          folders={folders}
          canDelete={canDelete}
          onClose={closeDialog}
          onDelete={() => setDialog({ kind: 'delete', assets: [dialog.asset] })}
        />
      ) : null}

      {dialog?.kind === 'pick' ? (
        <FolderPickerDialog
          mode={dialog.mode}
          count={dialog.ids.length}
          folders={folders}
          busy={busy}
          error={dialogError}
          onClose={closeDialog}
          onChoose={(folderId) => void organize(dialog.mode, dialog.ids, folderId)}
        />
      ) : null}

      {dialog?.kind === 'delete' ? (
        <ConfirmDialog
          title={dialog.assets.length === 1 ? 'Delete this file?' : `Delete ${countLabel(dialog.assets.length, 'file')}?`}
          confirmLabel="Delete"
          danger
          busy={busy}
          error={dialogError}
          onConfirm={() => void removeAssets(dialog.assets)}
          onCancel={closeDialog}
        >
          <p>
            {dialog.assets.length === 1 ? <strong>{dialog.assets[0]?.fileName}</strong> : 'These files'} will be deleted for good, with their folders and tags. This cannot
            be undone.
          </p>
        </ConfirmDialog>
      ) : null}

      {dialog?.kind === 'deleteFolder' ? (
        <ConfirmDialog
          title="Delete this folder?"
          confirmLabel="Delete folder"
          danger
          busy={busy}
          error={dialogError}
          onConfirm={() => void removeFolder(dialog.folder)}
          onCancel={closeDialog}
        >
          <p>
            <strong>{dialog.folder.name}</strong> will be deleted. The {countLabel(dialog.folder.assetCount, 'file')} in it are not deleted: they stay in the library.
          </p>
        </ConfirmDialog>
      ) : null}
    </div>
  );
}
