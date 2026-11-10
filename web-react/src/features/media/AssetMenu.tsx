import { useCallback, useRef, useState } from 'react';
import { mediaFileUrl, type MediaAsset } from '@/api/media';
import { UiIcon } from '@/components/icons/UiIcon';
import { useDismiss } from '@/lib/uiHooks';

export interface Abilities {
  /** medialibrary.create: put files in folders. */
  canCreate: boolean;
  /** medialibrary.delete: delete files. */
  canDelete: boolean;
}

export type AssetAction = 'open' | 'move' | 'copy' | 'delete';

/**
 * The "⋮" button of a file and the menu it opens: look at it, download it, and (for those who may) move it, copy it to a folder or delete
 * it. Download is a plain link to the file; the rest tell the page what was chosen, and the page does it. A file that content uses has no
 * Delete: the server would refuse it, so the menu does not offer it.
 */
export function AssetMenu({
  asset,
  abilities,
  onAction,
}: {
  asset: MediaAsset;
  abilities: Abilities;
  onAction: (action: AssetAction, asset: MediaAsset) => void;
}) {
  const [open, setOpen] = useState(false);
  const anchor = useRef<HTMLDivElement>(null);
  const button = useRef<HTMLButtonElement>(null);
  const close = useCallback(() => setOpen(false), []);
  useDismiss(open, close, anchor, button);

  const choose = (action: AssetAction) => {
    close();
    onAction(action, asset);
  };

  return (
    <div className="menu-anchor" ref={anchor}>
      <button
        className="button button-icon media-more"
        type="button"
        ref={button}
        aria-label={`Actions for ${asset.fileName}`}
        aria-haspopup="menu"
        aria-expanded={open}
        onClick={() => setOpen((value) => !value)}
      >
        <UiIcon name="more" />
      </button>

      {open ? (
        <ul className="menu-popover menu-popover-end" role="menu" aria-label={`Actions for ${asset.fileName}`}>
          <li role="none">
            <button role="menuitem" type="button" className="menu-item" onClick={() => choose('open')}>
              View details
            </button>
          </li>
          <li role="none">
            <a role="menuitem" className="menu-item" href={mediaFileUrl(asset.id, { download: true })} download={asset.fileName} onClick={close}>
              Download
            </a>
          </li>
          {abilities.canCreate ? (
            <>
              <li role="none">
                <button role="menuitem" type="button" className="menu-item" onClick={() => choose('move')}>
                  Move to folder…
                </button>
              </li>
              <li role="none">
                <button role="menuitem" type="button" className="menu-item" onClick={() => choose('copy')}>
                  Copy to folder…
                </button>
              </li>
            </>
          ) : null}
          {abilities.canDelete && asset.usageCount === 0 ? (
            <li role="none">
              <button role="menuitem" type="button" className="menu-item menu-item-danger" onClick={() => choose('delete')}>
                Delete
              </button>
            </li>
          ) : null}
        </ul>
      ) : null}
    </div>
  );
}
