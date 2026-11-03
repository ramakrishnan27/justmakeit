import { countLabel, pageSummary } from '@/lib/format';

/** Previous and Next buttons with "1–10 of 45 · 5 pages" between them. */
export function Pager({
  label,
  page,
  pageSize,
  totalCount,
  totalPages,
  onPage,
}: {
  /** Names the pager for a screen reader, for example "Organization pages". */
  label: string;
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
  onPage: (page: number) => void;
}) {
  return (
    <nav className="pager" aria-label={label}>
      <button className="button" type="button" disabled={page <= 1} onClick={() => onPage(page - 1)}>
        Previous
      </button>
      <p className="pager-summary">
        {pageSummary(page, pageSize, totalCount)} · {countLabel(totalPages, 'page')}
      </p>
      <button className="button" type="button" disabled={page >= totalPages} onClick={() => onPage(page + 1)}>
        Next
      </button>
    </nav>
  );
}
