import { useId } from 'react';
import { mediaFileUrl, type MediaAsset, type MediaFolder } from '@/api/media';
import { UiIcon } from '@/components/icons/UiIcon';
import { Modal } from '@/components/ui/Modal';
import { formatDate } from '@/lib/format';
import { dimensionsLabel, formatBytes, mediaTypeLabel, tagTone } from '@/lib/media';
import { MediaTypeIcon } from './MediaThumb';

/**
 * One file in full: the picture, the video or the sound itself, and what is known about it (kind, size, dimensions, when it was added, where
 * it is kept, its tags, whether anything uses it). A document has no preview here, so it offers the download. Delete is offered to those who
 * may and only for a file nothing uses; the page asks again before it deletes.
 */
export function AssetDialog({
  asset,
  folders,
  canDelete,
  onDelete,
  onClose,
}: {
  asset: MediaAsset;
  folders: readonly MediaFolder[];
  canDelete: boolean;
  onDelete: () => void;
  onClose: () => void;
}) {
  const titleId = useId();
  const url = mediaFileUrl(asset.id);
  const dimensions = dimensionsLabel(asset);
  const inFolders = asset.folderIds.map((id) => folders.find((folder) => folder.id === id)?.name).filter((name): name is string => name !== undefined);

  return (
    <Modal titleId={titleId} onClose={onClose}>
      <h2 id={titleId} className="media-dialog-title">
        {asset.fileName}
      </h2>

      <div className="media-preview">
        {asset.mediaType === 'image' ? (
          <img src={url} alt={asset.fileName} />
        ) : asset.mediaType === 'video' ? (
          // Controls, no autoplay. A caption track is not something a file carries, so there is none to offer.
          <video src={url} controls preload="metadata" playsInline aria-label={asset.fileName} />
        ) : asset.mediaType === 'other' && asset.mimeType.startsWith('audio/') ? (
          <audio src={url} controls preload="metadata" aria-label={asset.fileName} />
        ) : (
          <div className="media-glyph media-glyph-large">
            <MediaTypeIcon type={asset.mediaType} />
            <p className="muted small">There is no preview for this kind of file. Download it to open it.</p>
          </div>
        )}
      </div>

      <dl className="media-details">
        <div>
          <dt>Kind</dt>
          <dd>{mediaTypeLabel(asset.mediaType)}</dd>
        </div>
        <div>
          <dt>Size</dt>
          <dd>{formatBytes(asset.sizeBytes)}</dd>
        </div>
        {dimensions ? (
          <div>
            <dt>Dimensions</dt>
            <dd>{dimensions}</dd>
          </div>
        ) : null}
        <div>
          <dt>Added</dt>
          <dd>{formatDate(asset.createdAt, { withTime: true })}</dd>
        </div>
        <div>
          <dt>Folders</dt>
          <dd>{inFolders.length > 0 ? inFolders.join(', ') : 'Uncategorized'}</dd>
        </div>
        <div>
          <dt>Tags</dt>
          <dd>
            {asset.tags.length > 0 ? (
              <ul className="tag-list" aria-label="Tags">
                {asset.tags.map((tag) => (
                  <li key={tag.id} className="media-tag" data-tone={tagTone(tag.name)}>
                    #{tag.name}
                  </li>
                ))}
              </ul>
            ) : (
              'None'
            )}
          </dd>
        </div>
        <div>
          <dt>Used in</dt>
          <dd>{asset.usageCount === 0 ? 'Not used yet' : asset.usageCount === 1 ? '1 place' : `${asset.usageCount} places`}</dd>
        </div>
      </dl>

      <div className="confirm-actions">
        {canDelete && asset.usageCount === 0 ? (
          <button className="button button-danger" type="button" onClick={onDelete}>
            <UiIcon name="trash" />
            Delete
          </button>
        ) : null}
        <a className="button" href={mediaFileUrl(asset.id, { download: true })} download={asset.fileName}>
          <UiIcon name="download" />
          Download
        </a>
        <button className="button button-primary" type="button" onClick={onClose}>
          Done
        </button>
      </div>
    </Modal>
  );
}
