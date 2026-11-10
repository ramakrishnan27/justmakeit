// The date and time a person picks are the time on their own clock. An <input type="datetime-local"> holds "2026-11-10T09:00"
// with no zone; the server wants an instant. These two functions are the only place the two meet.

const pad = (value: number) => String(value).padStart(2, '0');

/** An instant as the value of a datetime-local input, in the viewer's own time zone. Empty for no date or a date that is not one. */
export function toLocalInput(iso: string | null | undefined): string {
  if (!iso) {
    return '';
  }
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) {
    return '';
  }
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}`;
}

/** The value of a datetime-local input as an instant, or null when it is empty or not a date. */
export function fromLocalInput(value: string): string | null {
  if (!/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}$/.test(value)) {
    return null;
  }
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? null : date.toISOString();
}

/** The viewer's own time zone as the browser names it (Asia/Kolkata), or UTC if it will not say. */
export function browserTimeZone(): string {
  try {
    return Intl.DateTimeFormat().resolvedOptions().timeZone || 'UTC';
  } catch {
    return 'UTC';
  }
}

/** The soonest a schedule can be set, as a datetime-local value: a few minutes ahead, so the server (which wants a minute) agrees. */
export function earliestLocalInput(now: Date = new Date(), minutes = 5): string {
  return toLocalInput(new Date(now.getTime() + minutes * 60 * 1000).toISOString());
}
