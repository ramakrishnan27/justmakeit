import { useState } from 'react';
import { Link } from 'react-router-dom';
import type { MediaFolder, MediaOverview } from '@/api/media';
import { UiIcon } from '@/components/icons/UiIcon';
import { ErrorNotice } from '@/components/ui/Notice';
import { formatNumber } from '@/lib/format';
import { messageForMediaLoadError } from '@/lib/messages';
import { tagTone } from '@/lib/media';
import type { LoadState } from './hooks';
import { searchFromFilters, UNCATEGORIZED, type MediaFilters } from './mediaFilters';

const TAGS_SHOWN = 6;

/**
 * The side panel: the folders of the workspace (All Media, each folder, Uncategorized) with how many files each holds, and the tags. Each
 * entry is a link to the same page with that folder or tag chosen. Making a folder needs medialibrary.create and deleting one
 * medialibrary.delete; the buttons are drawn only for those who hold them. Below 1024 px the lists are rows that scroll sideways.
 */
export function FolderPanel({
  state,
  filters,
  canCreate,
  canDelete,
  onCreateFolder,
  onDeleteFolder,
  onRetry,
}: {
  state: LoadState<MediaOverview>;
  filters: MediaFilters;
  canCreate: boolean;
  canDelete: boolean;
  onCreateFolder: () => void;
  onDeleteFolder: (folder: MediaFolder) => void;
  onRetry: () => void;
}) {
  const [allTags, setAllTags] = useState(false);
  const overview = state.status === 'ready' ? state.data : state.status === 'loading' ? state.previous : null;
  const to = (change: Partial<MediaFilters>) => ({ search: searchFromFilters({ ...filters, ...change, page: 1 }).toString() });
  const noTag = filters.tagId === '';

  const entry = (key: string, label: string, count: number | null, folder: string, folderRow?: MediaFolder) => (
    <li key={key} className="media-folder-item">
      <Link className="media-folder-link" to={to({ folder })} aria-current={filters.folder === folder ? 'page' : undefined}>
        <UiIcon name="folder" />
        <span className="media-folder-name">{label}</span>
        {count === null ? null : <span className="media-folder-count">{formatNumber(count)}</span>}
      </Link>
      {folderRow && canDelete ? (
        <button className="media-folder-delete" type="button" aria-label={`Delete folder ${folderRow.name}`} onClick={() => onDeleteFolder(folderRow)}>
          <UiIcon name="close" />
        </button>
      ) : null}
    </li>
  );

  const tags = overview?.tags ?? [];
  const shownTags = allTags ? tags : tags.slice(0, TAGS_SHOWN);

  return (
    <aside className="media-side" aria-label="Folders and tags">
      <section className="media-side-section" aria-labelledby="media-folders-title">
        <div className="media-side-head">
          <h2 id="media-folders-title">Folders</h2>
          {canCreate ? (
            <button className="button button-icon" type="button" aria-label="Create folder" onClick={onCreateFolder}>
              <UiIcon name="plus" />
            </button>
          ) : null}
        </div>

        {state.status === 'error' ? (
          <ErrorNotice message={messageForMediaLoadError(state.error, 'The folders')}>
            <button className="button" type="button" onClick={onRetry}>
              Try again
            </button>
          </ErrorNotice>
        ) : null}

        {overview ? (
          <ul className="media-folder-list">
            {entry('all', 'All Media', overview.allCount, '')}
            {overview.folders.map((folder) => entry(folder.id, folder.name, folder.assetCount, folder.id, folder))}
            {entry('none', 'Uncategorized', overview.uncategorizedCount, UNCATEGORIZED)}
          </ul>
        ) : state.status === 'loading' ? (
          <p className="muted" role="status">
            Loading folders…
          </p>
        ) : null}
      </section>

      {tags.length > 0 ? (
        <section className="media-side-section" aria-labelledby="media-tags-title">
          <div className="media-side-head">
            <h2 id="media-tags-title">Tags</h2>
            {tags.length > TAGS_SHOWN ? (
              <button className="button button-quiet" type="button" aria-expanded={allTags} onClick={() => setAllTags((value) => !value)}>
                {allTags ? 'Show fewer' : 'See all'}
              </button>
            ) : null}
          </div>
          <ul className="media-tag-list">
            {shownTags.map((tag) => (
              <li key={tag.id}>
                <Link
                  className="media-tag media-tag-link"
                  data-tone={tagTone(tag.name)}
                  to={to({ tagId: filters.tagId === tag.id ? '' : tag.id })}
                  aria-current={!noTag && filters.tagId === tag.id ? 'page' : undefined}
                >
                  #{tag.name} <span className="media-tag-count">{formatNumber(tag.assetCount)}</span>
                </Link>
              </li>
            ))}
          </ul>
        </section>
      ) : null}
    </aside>
  );
}
