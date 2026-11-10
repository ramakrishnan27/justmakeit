import { useEffect, useId, useState } from 'react';
import type { MediaAsset } from '@/api/media';
import { Modal } from '@/components/ui/Modal';
import { ErrorNotice, Loading } from '@/components/ui/Notice';
import { Pager } from '@/components/ui/Pager';
import { UiIcon } from '@/components/icons/UiIcon';
import { MediaThumb } from '@/features/media/MediaThumb';
import { useMediaList, useMediaOverview } from '@/features/media/hooks';
import { noFilters } from '@/features/media/mediaFilters';
import { formatBytes, mediaTypeLabel } from '@/lib/media';
import { messageForMediaLoadError } from '@/lib/messages';
import { useDebounced } from '@/lib/uiHooks';
import type { DraftMedia } from '../postDraft';

/** The kinds of file a post can carry, as tabs. */
const TABS = [
  { value: 'image', label: 'Images' },
  { value: 'video', label: 'Videos' },
] as const;

type Tab = (typeof TABS)[number]['value'];

/** A file of the library as the draft holds it (without a caption, which nothing edits yet). */
export function draftMediaFromAsset(asset: MediaAsset): Omit<DraftMedia, 'caption'> {
  return {
    assetId: asset.id,
    fileName: asset.fileName,
    mediaType: asset.mediaType,
    mimeType: asset.mimeType,
    sizeBytes: asset.sizeBytes,
    width: asset.width,
    height: asset.height,
  };
}

/**
 * Choose files from the Media Library for a post. It shows the library of the post's workspace, images or videos (a post carries
 * nothing else), with a search box and a folder; a tap on a file ticks it, and "Add" puts the ticked files on the post in the order
 * they were ticked. A file already on the post is shown as added and cannot be chosen twice; once the post is full the rest cannot be
 * ticked. The choices live in the dialog, not in the address: closing it leaves the editor as it was.
 */
export function MediaPickerDialog({
  workspaceId,
  attached,
  slots,
  onAdd,
  onClose,
}: {
  workspaceId: string;
  /** The files already on the post. */
  attached: readonly DraftMedia[];
  /** How many more files fit on the post. */
  slots: number;
  onAdd: (files: Array<Omit<DraftMedia, 'caption'>>) => void;
  onClose: () => void;
}) {
  const titleId = useId();
  const [tab, setTab] = useState<Tab>('image');
  const [folder, setFolder] = useState('');
  const [search, setSearch] = useState('');
  const [page, setPage] = useState(1);
  // In the order they were ticked, which is the order they go on the post.
  const [chosen, setChosen] = useState<readonly MediaAsset[]>([]);

  const q = useDebounced(search, 300);
  const { state, reload } = useMediaList({ ...noFilters, type: tab, folder, q, page }, workspaceId);
  const { state: overview } = useMediaOverview(workspaceId);
  const folders = overview.status === 'ready' ? overview.data.folders : [];

  // A new search, folder or tab starts at the first page.
  useEffect(() => setPage(1), [tab, folder, q]);

  const result = state.status === 'ready' ? state.data : state.status === 'loading' ? state.previous : null;
  const onPost = new Set(attached.map((file) => file.assetId));
  const isChosen = (id: string) => chosen.some((asset) => asset.id === id);
  const full = chosen.length >= slots;

  function toggle(asset: MediaAsset) {
    setChosen((current) => (current.some((entry) => entry.id === asset.id) ? current.filter((entry) => entry.id !== asset.id) : [...current, asset]));
  }

  return (
    <Modal titleId={titleId} onClose={onClose}>
      <h2 id={titleId}>Choose from the Media Library</h2>
      <p className="muted small">
        {slots === 0 ? 'The post already has the most files it can hold.' : `Tick up to ${slots} ${slots === 1 ? 'file' : 'files'} to add to the post.`}
      </p>

      <div className="picker-tabs" role="group" aria-label="Kind of file">
        {TABS.map((entry) => (
          <button key={entry.value} type="button" className="mini-tab" aria-pressed={tab === entry.value} onClick={() => setTab(entry.value)}>
            {entry.label}
            {result ? ` (${result.typeCounts[entry.value] ?? 0})` : ''}
          </button>
        ))}
      </div>

      <div className="picker-filters">
        <div className="search">
          <span className="search-icon">
            <UiIcon name="search" />
          </span>
          <input
            type="search"
            aria-label="Search files"
            placeholder="Search files…"
            autoComplete="off"
            maxLength={100}
            value={search}
            onChange={(event) => setSearch(event.target.value)}
          />
        </div>
        <div className="filter">
          <label className="visually-hidden" htmlFor="picker-folder">
            Folder
          </label>
          <select id="picker-folder" value={folder} onChange={(event) => setFolder(event.target.value)}>
            <option value="">All folders</option>
            {folders.map((entry) => (
              <option key={entry.id} value={entry.id}>
                {entry.name}
              </option>
            ))}
          </select>
        </div>
      </div>

      <div aria-live="polite" aria-busy={state.status === 'loading'}>
        {state.status === 'error' ? (
          <ErrorNotice message={messageForMediaLoadError(state.error)}>
            <button className="button" type="button" onClick={reload}>
              Try again
            </button>
          </ErrorNotice>
        ) : null}

        {state.status === 'loading' && !result ? <Loading text="Loading files…" /> : null}

        {result && result.items.length === 0 && state.status !== 'error' ? (
          <p className="notice content-empty">
            {q.trim() !== '' || folder !== ''
              ? 'No file matches what you chose.'
              : `There are no ${tab === 'image' ? 'images' : 'videos'} in this workspace's library yet. Use Upload to add some.`}
          </p>
        ) : null}

        {result && result.items.length > 0 && state.status !== 'error' ? (
          <>
            <ul className="media-grid picker-grid" aria-label="Files">
              {result.items.map((asset) => {
                const added = onPost.has(asset.id);
                const picked = isChosen(asset.id);
                return (
                  <li key={asset.id} className="media-tile" data-selected={picked ? 'true' : undefined}>
                    <button
                      type="button"
                      className="media-open picker-pick"
                      aria-pressed={picked}
                      disabled={added || (!picked && full)}
                      aria-label={`${asset.fileName}${added ? ' (already on the post)' : ''}`}
                      onClick={() => toggle(asset)}
                    >
                      <span className="media-thumb">
                        <MediaThumb asset={asset} />
                      </span>
                      {added ? <span className="picker-badge">Added</span> : picked ? <span className="picker-badge picker-badge-on">{chosen.findIndex((entry) => entry.id === asset.id) + 1}</span> : null}
                    </button>
                    <div className="media-info">
                      <div className="media-info-text">
                        <span className="media-name" title={asset.fileName}>
                          {asset.fileName}
                        </span>
                        <span className="media-sub">
                          {mediaTypeLabel(asset.mediaType)} · {formatBytes(asset.sizeBytes)}
                        </span>
                      </div>
                    </div>
                  </li>
                );
              })}
            </ul>
            <Pager label="Library pages" page={result.page} pageSize={result.pageSize} totalCount={result.totalCount} totalPages={result.totalPages} onPage={setPage} />
          </>
        ) : null}
      </div>

      <div className="confirm-actions picker-footer">
        <p className="muted small picker-count" role="status">
          {chosen.length === 0 ? 'Nothing ticked yet.' : `${chosen.length} ticked.`}
        </p>
        <button className="button" type="button" onClick={onClose}>
          Cancel
        </button>
        <button
          className="button button-primary"
          type="button"
          disabled={chosen.length === 0}
          onClick={() => {
            onAdd(chosen.map(draftMediaFromAsset));
            onClose();
          }}
        >
          {chosen.length === 0 ? 'Add' : chosen.length === 1 ? 'Add 1 file' : `Add ${chosen.length} files`}
        </button>
      </div>
    </Modal>
  );
}
