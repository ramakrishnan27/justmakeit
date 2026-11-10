import { MEDIA_SORTS } from '@/api/media';
import { UiIcon } from '@/components/icons/UiIcon';
import type { MediaView } from './mediaFilters';

/**
 * The bar above the files: a box that ticks every file of the page, how many are ticked, Move / Copy / Delete for the ticked ones (each
 * drawn only for those who may, and off while nothing is ticked or something is being done), the grid or list switch and the order.
 */
export function SelectionBar({
  selectable,
  count,
  total,
  canCreate,
  canDelete,
  busy,
  view,
  sort,
  onToggleAll,
  onMove,
  onCopy,
  onDelete,
  onView,
  onSort,
}: {
  /** Whether there is anything to do with a ticked file; without it the box and the count are not drawn. */
  selectable: boolean;
  count: number;
  total: number;
  canCreate: boolean;
  canDelete: boolean;
  busy: boolean;
  view: MediaView;
  sort: string;
  onToggleAll: () => void;
  onMove: () => void;
  onCopy: () => void;
  onDelete: () => void;
  onView: (view: MediaView) => void;
  onSort: (sort: string) => void;
}) {
  const none = count === 0 || busy;
  return (
    <div className="media-toolbar">
      {selectable ? (
        <div className="media-selection">
          <label className="media-check media-check-inline">
            <input
              type="checkbox"
              checked={total > 0 && count === total}
              ref={(box) => {
                if (box) {
                  box.indeterminate = count > 0 && count < total;
                }
              }}
              onChange={onToggleAll}
              disabled={total === 0}
            />
            <span className="visually-hidden">Select all files on this page</span>
          </label>
          <span className="media-selected" aria-live="polite">
            {count} selected
          </span>
          {canCreate ? (
            <>
              <button className="button" type="button" disabled={none} onClick={onMove}>
                Move
              </button>
              <button className="button" type="button" disabled={none} onClick={onCopy}>
                Copy
              </button>
            </>
          ) : null}
          {canDelete ? (
            <button className="button" type="button" disabled={none} onClick={onDelete}>
              <UiIcon name="trash" />
              Delete
            </button>
          ) : null}
        </div>
      ) : (
        <span />
      )}

      <div className="media-view-controls">
        <div className="media-view-switch" role="group" aria-label="Layout">
          <button className="button button-icon" type="button" aria-pressed={view === 'grid'} aria-label="Grid view" onClick={() => onView('grid')}>
            <UiIcon name="grid" />
          </button>
          <button className="button button-icon" type="button" aria-pressed={view === 'list'} aria-label="List view" onClick={() => onView('list')}>
            <UiIcon name="rows" />
          </button>
        </div>
        <div className="filter">
          <label className="visually-hidden" htmlFor="media-sort">
            Sort by
          </label>
          <select id="media-sort" value={sort} onChange={(event) => onSort(event.target.value)}>
            {MEDIA_SORTS.map((entry) => (
              <option key={entry.value} value={entry.value}>
                Sort by: {entry.label}
              </option>
            ))}
          </select>
        </div>
      </div>
    </div>
  );
}
