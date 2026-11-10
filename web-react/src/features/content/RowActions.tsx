import { useCallback, useRef, useState } from 'react';
import { Link } from 'react-router-dom';
import type { PostListItem } from '@/api/content';
import { useDismiss } from '@/lib/uiHooks';
import { actionLabels, availableActions, type Abilities, type RowAction } from './rowActionRules';

/**
 * The "…" button of a row and the menu it opens. Edit (or Open) is a link to the post; the others are buttons that tell the page
 * what was chosen, and the page does it. While something is being done to a post the button is off.
 */
export function RowActions({
  post,
  abilities,
  busy,
  onAction,
}: {
  post: PostListItem;
  abilities: Abilities;
  busy: boolean;
  onAction: (action: Exclude<RowAction, 'edit' | 'open'>, post: PostListItem) => void;
}) {
  const [open, setOpen] = useState(false);
  const anchor = useRef<HTMLDivElement>(null);
  const button = useRef<HTMLButtonElement>(null);
  const close = useCallback(() => setOpen(false), []);
  useDismiss(open, close, anchor, button);

  const name = post.title.length > 0 ? post.title : 'untitled post';

  return (
    <div className="menu-anchor" ref={anchor}>
      <button
        className="button button-icon"
        type="button"
        ref={button}
        aria-label={`Actions for ${name}`}
        aria-haspopup="menu"
        aria-expanded={open}
        disabled={busy}
        onClick={() => setOpen((value) => !value)}
      >
        <span aria-hidden="true">•••</span>
      </button>

      {open ? (
        <ul className="menu-popover menu-popover-end" role="menu" aria-label={`Actions for ${name}`}>
          {availableActions(post.status, abilities).map((action) => (
            <li key={action} role="none">
              {action === 'edit' || action === 'open' ? (
                <Link role="menuitem" className="menu-item" to={`/content-studio/${post.id}`} onClick={close}>
                  {actionLabels[action]}
                </Link>
              ) : (
                <button
                  role="menuitem"
                  type="button"
                  className={`menu-item${action === 'delete' ? ' menu-item-danger' : ''}`}
                  onClick={() => {
                    close();
                    onAction(action, post);
                  }}
                >
                  {actionLabels[action]}
                </button>
              )}
            </li>
          ))}
        </ul>
      ) : null}
    </div>
  );
}
