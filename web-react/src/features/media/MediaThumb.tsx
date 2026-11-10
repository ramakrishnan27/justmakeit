import { useState } from 'react';
import { mediaFileUrl } from '@/api/media';
import { Icon } from '@/components/icons/Icon';
import { UiIcon } from '@/components/icons/UiIcon';
import { extensionOf, formatDuration } from '@/lib/media';

/** The small drawing of a kind of file: a picture, a film, a page or a note. */
export function MediaTypeIcon({ type }: { type: string }) {
  if (type === 'image') {
    return <Icon name="image" />;
  }
  return <UiIcon name={type === 'video' ? 'video' : type === 'document' ? 'file' : 'music'} />;
}

/**
 * What a file looks like in the list. An image is the image itself, cropped to fill the space (the server sends the original; making
 * smaller copies needs an image library). A video shows its first frame and its length, which the browser reads from the file. A document
 * or a sound has no picture, so it shows a drawing and its extension. A picture that cannot be loaded falls back to the drawing too.
 * It is for looking at: the button or link around it carries the name.
 */
export function MediaThumb({ asset }: { asset: { id: string; mediaType: string; fileName: string } }) {
  const [length, setLength] = useState<string | null>(null);
  const [broken, setBroken] = useState(false);
  const url = mediaFileUrl(asset.id);

  if (asset.mediaType === 'image' && !broken) {
    return <img className="media-img" src={url} alt="" loading="lazy" decoding="async" onError={() => setBroken(true)} />;
  }

  if (asset.mediaType === 'video' && !broken) {
    return (
      <>
        {/* #t=0.1 asks for a frame just after the start, which is what shows while nothing plays; the server never sees the fragment. */}
        <video
          className="media-img"
          src={`${url}#t=0.1`}
          muted
          playsInline
          preload="metadata"
          tabIndex={-1}
          aria-hidden="true"
          onLoadedMetadata={(event) => setLength(formatDuration(event.currentTarget.duration))}
          onError={() => setBroken(true)}
        />
        <span className="media-play" aria-hidden="true">
          <UiIcon name="play" />
        </span>
        {length ? <span className="media-duration">{length}</span> : null}
      </>
    );
  }

  const extension = extensionOf(asset.fileName).slice(1).toUpperCase();
  return (
    <span className="media-glyph" aria-hidden="true">
      <MediaTypeIcon type={asset.mediaType} />
      {extension ? <span className="media-ext">{extension}</span> : null}
    </span>
  );
}
