import { useState } from 'react';
import type { MediaAsset } from '@/api/media';
import { useCan } from '@/access/hooks';
import { PERMISSIONS } from '@/access/permissions';
import { ErrorNotice, Loading } from '@/components/ui/Notice';
import { MediaThumb } from '@/features/media/MediaThumb';
import { UploadDialog } from '@/features/media/UploadDialog';
import { useMediaConfig, useMediaOverview } from '@/features/media/hooks';
import { dimensionsLabel, formatBytes, mediaTypeLabel } from '@/lib/media';
import { messageForMediaLoadError } from '@/lib/messages';
import { MAX_MEDIA, POST_MEDIA_TYPES, addMedia, moveMedia, removeMedia, type DraftMedia } from '../postDraft';
import { MediaPickerDialog, draftMediaFromAsset } from './MediaPickerDialog';

/** What the upload dialog needs and reads for itself: the workspace's folders and the size limit. */
function UploadForPost({ workspaceId, onUploaded, onClose }: { workspaceId: string; onUploaded: (assets: MediaAsset[]) => void; onClose: () => void }) {
  const { state: overview, reload } = useMediaOverview(workspaceId);
  const { state: config } = useMediaConfig();

  if (overview.status === 'ready' && config.status === 'ready') {
    return <UploadDialog workspaceId={workspaceId} folders={overview.data.folders} defaultFolderId="" config={config.data} onUploaded={onUploaded} onClose={onClose} />;
  }

  const failed = overview.status === 'error' ? overview.error : config.status === 'error' ? config.error : null;
  return failed ? (
    <ErrorNotice message={messageForMediaLoadError(failed)}>
      <button className="button" type="button" onClick={reload}>
        Try again
      </button>
      <button className="button" type="button" onClick={onClose}>
        Close
      </button>
    </ErrorNotice>
  ) : (
    <Loading text="Getting ready to upload…" />
  );
}

/**
 * "Add Media": the files of the Media Library on this post. **Media Library** opens the picker, **Upload** adds new files to the
 * library and puts them straight on the post, and **AI Generate** is switched off until the assistant exists. Below are the files
 * on the post in the order they will show, each with buttons to take it off or move it earlier or later (buttons, so none of this
 * needs dragging). The panel is switched off, and says why, where there is no library or the person's role cannot look into it.
 */
export function MediaPanel({
  workspaceId,
  media,
  setMedia,
  available,
  problem,
}: {
  workspaceId: string;
  media: readonly DraftMedia[];
  /** Changes the list from what it is now, so two changes made one after the other both count. */
  setMedia: (change: (current: readonly DraftMedia[]) => readonly DraftMedia[]) => void;
  /** Whether there is a Media Library at all (`capabilities.media`). */
  available: boolean;
  /** What the server said about the files, if it refused them. */
  problem?: string | null;
}) {
  const canView = useCan({ permission: PERMISSIONS.mediaLibraryView });
  const canUpload = useCan({ allOf: [PERMISSIONS.mediaLibraryView, PERMISSIONS.mediaLibraryCreate] });
  const [dialog, setDialog] = useState<'library' | 'upload' | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  const usable = available && canView;
  const slots = Math.max(0, MAX_MEDIA - media.length);
  const reason = !available
    ? 'The Media Library is not available.'
    : !canView
      ? 'Your role cannot use the Media Library, so files cannot be added.'
      : slots === 0
        ? `A post can hold ${MAX_MEDIA} files, and this one has them all.`
        : null;

  function addAll(files: Array<Omit<DraftMedia, 'caption'>>) {
    setNotice(null);
    setMedia((current) => addMedia(current, files));
  }

  function afterUpload(uploaded: MediaAsset[]) {
    const fits = uploaded.filter((asset) => (POST_MEDIA_TYPES as readonly string[]).includes(asset.mediaType));
    addAll(fits.map(draftMediaFromAsset));
    const left = uploaded.length - fits.length;
    setNotice(left > 0 ? `${left === 1 ? 'A file was' : `${left} files were`} added to the library but not to the post: only images and videos can go on a post.` : null);
  }

  return (
    <section className={`panel${usable ? '' : ' panel-off'}`} aria-labelledby="panel-media">
      <h2 id="panel-media">Add Media</h2>

      <div className="mini-tabs" role="group" aria-label="Where media comes from">
        <button type="button" className="mini-tab" disabled={!usable || !canUpload || slots === 0} onClick={() => setDialog('upload')}>
          Upload
        </button>
        <button type="button" className="mini-tab" disabled={!usable || slots === 0} onClick={() => setDialog('library')}>
          Media Library
        </button>
        <button type="button" className="mini-tab" disabled title="Coming soon">
          AI Generate
        </button>
      </div>

      <p className="muted small">
        {reason ?? (media.length === 0 ? `Add images or videos, up to ${MAX_MEDIA}.` : `${media.length} of ${MAX_MEDIA} files.`)}
        {usable && !canUpload && slots > 0 ? ' Your role can choose files but not upload new ones.' : ''}
      </p>

      <div aria-live="polite">
        {notice ? (
          <p className="muted small" role="status">
            {notice}
          </p>
        ) : null}
        {problem ? (
          <p className="form-error" role="alert">
            {problem}
          </p>
        ) : null}
      </div>

      {media.length > 0 ? (
        <ul className="media-attached" aria-label="Files on this post">
          {media.map((file, index) => {
            const size = dimensionsLabel(file);
            return (
              <li key={file.assetId} className="media-attached-item">
                <span className="media-attached-thumb">
                  <MediaThumb asset={{ id: file.assetId, mediaType: file.mediaType, fileName: file.fileName }} />
                </span>
                <div className="media-attached-text">
                  <span className="media-name" title={file.fileName}>
                    {file.fileName.length > 0 ? file.fileName : 'A file of the library'}
                  </span>
                  <span className="media-sub">
                    {[file.mediaType ? mediaTypeLabel(file.mediaType) : null, file.sizeBytes > 0 ? formatBytes(file.sizeBytes) : null, size].filter(Boolean).join(' · ')}
                  </span>
                </div>
                <div className="media-attached-actions">
                  <button
                    type="button"
                    className="button button-icon"
                    aria-label={`Move ${file.fileName || 'the file'} earlier`}
                    disabled={index === 0}
                    onClick={() => setMedia((current) => moveMedia(current, file.assetId, -1))}
                  >
                    <span aria-hidden="true">↑</span>
                  </button>
                  <button
                    type="button"
                    className="button button-icon"
                    aria-label={`Move ${file.fileName || 'the file'} later`}
                    disabled={index === media.length - 1}
                    onClick={() => setMedia((current) => moveMedia(current, file.assetId, 1))}
                  >
                    <span aria-hidden="true">↓</span>
                  </button>
                  <button
                    type="button"
                    className="button button-icon"
                    aria-label={`Remove ${file.fileName || 'the file'} from the post`}
                    onClick={() => setMedia((current) => removeMedia(current, file.assetId))}
                  >
                    <span aria-hidden="true">×</span>
                  </button>
                </div>
              </li>
            );
          })}
        </ul>
      ) : null}

      {dialog === 'library' ? <MediaPickerDialog workspaceId={workspaceId} attached={media} slots={slots} onAdd={addAll} onClose={() => setDialog(null)} /> : null}
      {dialog === 'upload' ? <UploadForPost workspaceId={workspaceId} onUploaded={afterUpload} onClose={() => setDialog(null)} /> : null}
    </section>
  );
}
