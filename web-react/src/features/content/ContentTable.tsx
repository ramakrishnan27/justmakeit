import { Link } from 'react-router-dom';
import type { PostListItem } from '@/api/content';
import { Badge } from '@/components/ui/Badge';
import { contentTypeLabel, statusDetail, statusLabel, statusTone } from '@/lib/content';
import { formatDate } from '@/lib/format';
import { ContentTypeIcon, PlatformIcon } from './icons';
import { RowActions } from './RowActions';
import type { Abilities, RowAction } from './rowActionRules';

/** A status as a coloured badge with its word, and, under it, when it goes out or went out. */
export function StatusCell({ post }: { post: PostListItem }) {
  const detail = statusDetail(post);
  return (
    <>
      <Badge tone={statusTone(post.status)}>{statusLabel(post.status)}</Badge>
      {detail ? <span className="status-detail">{detail}</span> : null}
    </>
  );
}

/**
 * The list as a table on a wide screen and as a stack of cards on a narrow one (the same markup, laid out by the stylesheet): the
 * post's title (a link to it) with its excerpt and tags, its status, the platforms it goes to, the stats (none are kept yet), when it
 * was made, and the actions menu.
 */
export function ContentTable({
  items,
  abilities,
  busyId,
  onAction,
}: {
  items: readonly PostListItem[];
  abilities: Abilities;
  busyId: string | null;
  onAction: (action: Exclude<RowAction, 'edit' | 'open'>, post: PostListItem) => void;
}) {
  return (
    <table className="content-table">
      <caption className="visually-hidden">Content</caption>
      <thead>
        <tr>
          <th scope="col">Content Details</th>
          <th scope="col">Status</th>
          <th scope="col">Platforms</th>
          <th scope="col">Stats</th>
          <th scope="col">Created On</th>
          <th scope="col">
            <span className="visually-hidden">Actions</span>
          </th>
        </tr>
      </thead>
      <tbody>
        {items.map((post) => (
          <tr key={post.id}>
            <td className="cell-details">
              <span className="content-thumb" title={contentTypeLabel(post.contentType)}>
                <ContentTypeIcon type={post.contentType} />
                <span className="visually-hidden">{contentTypeLabel(post.contentType)}</span>
              </span>
              <div className="content-text">
                <Link className="content-title" to={`/content-studio/${post.id}`}>
                  {post.title.length > 0 ? post.title : 'Untitled'}
                </Link>
                {post.excerpt.length > 0 ? <p className="content-excerpt">{post.excerpt}</p> : null}
                {post.tags.length > 0 ? (
                  <ul className="tag-list" aria-label="Tags">
                    {post.tags.map((tag) => (
                      <li key={tag.id} className="tag" data-color={tag.color}>
                        {tag.name}
                      </li>
                    ))}
                  </ul>
                ) : null}
              </div>
            </td>
            <td className="cell-status" data-label="Status">
              <StatusCell post={post} />
            </td>
            <td className="cell-platforms" data-label="Platforms">
              {post.platforms.length === 0 ? (
                <span className="muted">–</span>
              ) : (
                <ul className="platform-list" aria-label="Platforms">
                  {post.platforms.map((target) => (
                    <li key={target.platform}>
                      <PlatformIcon platform={target.platform} />
                    </li>
                  ))}
                </ul>
              )}
            </td>
            <td className="cell-stats" data-label="Stats">
              <span className="muted" title="Statistics are not available yet">
                –
              </span>
            </td>
            <td className="cell-created" data-label="Created">
              <time dateTime={post.createdAt}>{formatDate(post.createdAt, { withTime: true })}</time>
            </td>
            <td className="cell-actions">
              <RowActions post={post} abilities={abilities} busy={busyId === post.id} onAction={onAction} />
            </td>
          </tr>
        ))}
      </tbody>
    </table>
  );
}
