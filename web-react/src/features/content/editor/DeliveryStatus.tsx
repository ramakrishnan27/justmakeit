import type { PostPlatformItem } from '@/api/content';
import { Badge } from '@/components/ui/Badge';
import { platformName, statusLabel, statusTone } from '@/lib/content';
import { formatDate } from '@/lib/format';
import { PlatformIcon } from '../icons';

/**
 * How a post is doing on each platform it goes to: pending, scheduled (and when), publishing, published (with a link to it),
 * or failed (with why, and how many times it was tried). Nothing is shown for a post whose platforms are all still pending: there is
 * nothing to report yet.
 */
export function DeliveryStatus({ targets }: { targets: readonly PostPlatformItem[] }) {
  if (targets.length === 0 || targets.every((target) => target.publishStatus === 'pending')) {
    return null;
  }

  return (
    <section className="panel" aria-labelledby="panel-delivery">
      <h2 id="panel-delivery">Delivery</h2>
      <ul className="delivery-list">
        {targets.map((target) => (
          <li key={target.platform}>
            <PlatformIcon platform={target.platform} decorative />
            <strong>{platformName(target.platform)}</strong>
            <Badge tone={statusTone(target.publishStatus)}>{statusLabel(target.publishStatus)}</Badge>
            <span className="muted small">
              {target.publishStatus === 'scheduled' && target.scheduledAt ? `for ${formatDate(target.scheduledAt, { withTime: true })}` : null}
              {target.publishStatus === 'published' && target.publishedAt ? formatDate(target.publishedAt, { withTime: true }) : null}
              {target.retryCount > 0 ? ` · ${target.retryCount} ${target.retryCount === 1 ? 'attempt' : 'attempts'} failed` : null}
            </span>
            {target.externalPostUrl ? (
              <a href={target.externalPostUrl} target="_blank" rel="noopener noreferrer nofollow">
                View post
              </a>
            ) : null}
            {target.lastError ? <p className="form-error">{target.lastError}</p> : null}
          </li>
        ))}
      </ul>
    </section>
  );
}
