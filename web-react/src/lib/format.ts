// Display helpers. They take plain values and return strings, so they are easy to test.

const numberFormat = new Intl.NumberFormat('en');

export function formatNumber(value: number): string {
  return Number.isFinite(value) ? numberFormat.format(value) : '–';
}

/** @param value An ISO string, a timestamp or a Date. timeZone defaults to the viewer's own. */
export function formatDate(
  value: string | number | Date | null | undefined,
  { timeZone, withTime = false }: { timeZone?: string; withTime?: boolean } = {},
): string {
  const date = value instanceof Date ? value : new Date(value ?? NaN);
  if (Number.isNaN(date.getTime())) {
    return '–';
  }

  return new Intl.DateTimeFormat('en', {
    dateStyle: 'medium',
    ...(withTime ? { timeStyle: 'short' as const } : {}),
    ...(timeZone ? { timeZone } : {}),
  }).format(date);
}

/** A plan name from the API (`trial`, `paid`) as a label. Unknown plans are shown as given, capitalised. */
export function planLabel(plan: string | null | undefined): string {
  const text = typeof plan === 'string' ? plan : '';
  return text.length === 0 ? '–' : text.charAt(0).toUpperCase() + text.slice(1);
}

const businessModelLabels: Record<string, string> = { company: 'Company', agency: 'Marketing agency', creator: 'Individual creator' };

/**
 * A business model from the API (`company`, `agency`, `creator`) as a label. A model this client does
 * not know is shown as given, capitalised, so a newer server never leaves a blank.
 */
export function businessModelLabel(model: string | null | undefined): string {
  if (typeof model !== 'string' || model.length === 0) {
    return '–';
  }
  return Object.hasOwn(businessModelLabels, model) ? (businessModelLabels[model] ?? model) : model.charAt(0).toUpperCase() + model.slice(1);
}

/** "1 member" or "3 members". */
export function countLabel(count: number, singular: string, plural = `${singular}s`): string {
  return `${formatNumber(count)} ${count === 1 ? singular : plural}`;
}
