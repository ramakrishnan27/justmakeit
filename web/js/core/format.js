// Display helpers. They take plain values and return strings, so they are easy to test.

const numberFormat = new Intl.NumberFormat('en');

export function formatNumber(value) {
  return Number.isFinite(value) ? numberFormat.format(value) : '–';
}

/**
 * @param {string | number | Date | null | undefined} value An ISO string, a timestamp or a Date.
 * @param {{ timeZone?: string, withTime?: boolean }} [options] timeZone defaults to the viewer's own.
 */
export function formatDate(value, { timeZone, withTime = false } = {}) {
  const date = value instanceof Date ? value : new Date(value ?? NaN);
  if (Number.isNaN(date.getTime())) {
    return '–';
  }

  return new Intl.DateTimeFormat('en', {
    dateStyle: 'medium',
    ...(withTime ? { timeStyle: 'short' } : {}),
    ...(timeZone ? { timeZone } : {}),
  }).format(date);
}

/** A plan name from the API (`trial`, `paid`) as a label. Unknown plans are shown as given, capitalised. */
export function planLabel(plan) {
  const text = typeof plan === 'string' ? plan : '';
  return text.length === 0 ? '–' : text.charAt(0).toUpperCase() + text.slice(1);
}

/** "1–20 of 45", or "0 results" when there is nothing. */
export function pageSummary(page, pageSize, totalCount) {
  if (!(totalCount > 0)) {
    return '0 results';
  }
  const first = (page - 1) * pageSize + 1;
  const last = Math.min(page * pageSize, totalCount);
  return first > totalCount ? `0 of ${formatNumber(totalCount)}` : `${formatNumber(first)}–${formatNumber(last)} of ${formatNumber(totalCount)}`;
}

/** "1 member" or "3 members". */
export function countLabel(count, singular, plural = `${singular}s`) {
  return `${formatNumber(count)} ${count === 1 ? singular : plural}`;
}
