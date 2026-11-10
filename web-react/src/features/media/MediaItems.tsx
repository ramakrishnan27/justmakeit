import type { MediaAsset } from '@/api/media';
import { formatDate } from '@/lib/format';
import { dimensionsLabel, formatBytes, mediaTypeLabel, tagTone } from '@/lib/media';
import { AssetMenu, type Abilities, type AssetAction } from './AssetMenu';
import { MediaThumb } from './MediaThumb';

interface ItemProps {
  /** Whether there is anything to do with a ticked file: tick boxes are drawn only then. */
  selectable: boolean;
  asset: MediaAsset;
  selected: boolean;
  abilities: Abilities;
  onToggle: (id: string) => void;
  onAction: (action: AssetAction, asset: MediaAsset) => void;
}

/** One tick box that names the file it is for. The box is 44 px square, as every control is, and sits on the corner of the file. */
function SelectBox({ asset, selected, onToggle }: Pick<ItemProps, 'asset' | 'selected' | 'onToggle'>) {
  return (
    <label className="media-check">
      <input type="checkbox" checked={selected} onChange={() => onToggle(asset.id)} />
      <span className="visually-hidden">Select {asset.fileName}</span>
    </label>
  );
}

/** The files as tiles (the picture, the name, the size and the date). */
export function MediaGrid({ items, ...rest }: { items: readonly MediaAsset[] } & Omit<ItemProps, 'asset' | 'selected'> & { selection: ReadonlySet<string> }) {
  const { selection, ...props } = rest;
  return (
    <ul className="media-grid" aria-label="Files">
      {items.map((asset) => (
        <li key={asset.id} className="media-tile" data-selected={selection.has(asset.id) ? 'true' : undefined}>
          {props.selectable ? <SelectBox asset={asset} selected={selection.has(asset.id)} onToggle={props.onToggle} /> : null}
          <button type="button" className="media-open" aria-label={`View ${asset.fileName}`} onClick={() => props.onAction('open', asset)}>
            <span className="media-thumb">
              <MediaThumb asset={asset} />
            </span>
          </button>
          <div className="media-info">
            <div className="media-info-text">
              <span className="media-name" title={asset.fileName}>
                {asset.fileName}
              </span>
              <span className="media-sub">
                {formatBytes(asset.sizeBytes)} · {formatDate(asset.createdAt)}
              </span>
            </div>
            <AssetMenu asset={asset} abilities={props.abilities} onAction={props.onAction} />
          </div>
        </li>
      ))}
    </ul>
  );
}

/** The same files as rows: a small picture, the name with its tags, and the kind, size, dimensions and date beside it. */
export function MediaRows({ items, ...rest }: { items: readonly MediaAsset[] } & Omit<ItemProps, 'asset' | 'selected'> & { selection: ReadonlySet<string> }) {
  const { selection, ...props } = rest;
  return (
    <ul className="media-rows" aria-label="Files">
      {items.map((asset) => {
        const dimensions = dimensionsLabel(asset);
        return (
          <li key={asset.id} className="media-row" data-selected={selection.has(asset.id) ? 'true' : undefined}>
            {props.selectable ? <SelectBox asset={asset} selected={selection.has(asset.id)} onToggle={props.onToggle} /> : null}
            <button type="button" className="media-open media-row-thumb" aria-label={`View ${asset.fileName}`} onClick={() => props.onAction('open', asset)}>
              <span className="media-thumb">
                <MediaThumb asset={asset} />
              </span>
            </button>
            <div className="media-row-text">
              <span className="media-name" title={asset.fileName}>
                {asset.fileName}
              </span>
              <span className="media-sub">
                {mediaTypeLabel(asset.mediaType)} · {formatBytes(asset.sizeBytes)}
                {dimensions ? ` · ${dimensions}` : ''} · {formatDate(asset.createdAt)}
              </span>
              {asset.tags.length > 0 ? (
                <ul className="tag-list" aria-label="Tags">
                  {asset.tags.map((tag) => (
                    <li key={tag.id} className="media-tag" data-tone={tagTone(tag.name)}>
                      #{tag.name}
                    </li>
                  ))}
                </ul>
              ) : null}
            </div>
            <AssetMenu asset={asset} abilities={props.abilities} onAction={props.onAction} />
          </li>
        );
      })}
    </ul>
  );
}
