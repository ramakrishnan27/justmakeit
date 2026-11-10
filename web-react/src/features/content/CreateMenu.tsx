import { useCallback, useRef, useState } from 'react';
import { Link } from 'react-router-dom';
import { CONTENT_TYPES } from '@/api/content';
import { UiIcon } from '@/components/icons/UiIcon';
import { useDismiss } from '@/lib/uiHooks';
import { ContentTypeIcon } from './icons';

/**
 * "Create New Content": a button that opens the kinds of content, each a link to the Create page for that kind. Escape closes it
 * and gives focus back to the button; so does a press anywhere else.
 */
export function CreateMenu({ workspaceId }: { workspaceId?: string }) {
  const [open, setOpen] = useState(false);
  const anchor = useRef<HTMLDivElement>(null);
  const button = useRef<HTMLButtonElement>(null);
  const close = useCallback(() => setOpen(false), []);
  useDismiss(open, close, anchor, button);

  return (
    <div className="menu-anchor" ref={anchor}>
      <button
        className="button button-primary"
        type="button"
        ref={button}
        aria-haspopup="menu"
        aria-expanded={open}
        onClick={() => setOpen((value) => !value)}
      >
        <UiIcon name="plus" />
        Create New Content
        <UiIcon name="chevron" />
      </button>

      {open ? (
        <ul className="menu-popover menu-popover-end" role="menu" aria-label="Kind of content">
          {CONTENT_TYPES.map((type) => (
            <li key={type.value} role="none">
              <Link
                role="menuitem"
                className="menu-item"
                to={`/content-studio/new?type=${type.value}${workspaceId ? `&workspace=${workspaceId}` : ''}`}
                onClick={close}
              >
                <ContentTypeIcon type={type.value} />
                {type.label}
              </Link>
            </li>
          ))}
        </ul>
      ) : null}
    </div>
  );
}
