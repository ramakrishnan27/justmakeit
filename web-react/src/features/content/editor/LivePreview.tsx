import { useState } from 'react';
import { MediaThumb } from '@/features/media/MediaThumb';
import { ContentTypeIcon } from '../icons';
import type { PostDraft } from '../postDraft';
import { RichTextView } from '../richText/RichTextView';
import { isDocEmpty } from '../richText/doc';

type Device = 'desktop' | 'mobile';

/**
 * What the post will look like, drawn from the draft as it is being written. A generic post card: the account, the title, the text,
 * the hashtags, the buttons, the poll and the place. It is not a copy of any platform's look. Desktop and mobile only change how
 * wide the card is. The text is drawn by `RichTextView` from the document, so nothing typed can become markup.
 */
export function LivePreview({ draft, accountName, framed = true }: { draft: PostDraft; accountName: string; framed?: boolean }) {
  const [device, setDevice] = useState<Device>('desktop');
  const empty = isDocEmpty(draft.bodyDoc) && draft.title.trim().length === 0 && draft.media.length === 0;
  const first = draft.media[0];
  const poll = draft.poll && draft.poll.options.some((option) => option.trim().length > 0) ? draft.poll : null;
  const handle = accountName.toLowerCase().replace(/[^a-z0-9]+/g, '');

  return (
    <section className={framed ? 'panel' : undefined} aria-labelledby="panel-preview">
      <div className="panel-head">
        <h2 id="panel-preview">Live Preview</h2>
        <div className="device-toggle" role="group" aria-label="Preview size">
          <button type="button" aria-pressed={device === 'desktop'} onClick={() => setDevice('desktop')}>
            Desktop
          </button>
          <button type="button" aria-pressed={device === 'mobile'} onClick={() => setDevice('mobile')}>
            Mobile
          </button>
        </div>
      </div>

      <article className={`preview-card preview-${device}`} aria-label="Post preview">
        <header className="preview-account">
          <span className="preview-avatar" aria-hidden="true">
            {accountName.charAt(0).toUpperCase() || '?'}
          </span>
          <div>
            <strong>{accountName}</strong>
            <span className="muted small">
              {handle ? `@${handle}` : ''} · now
            </span>
          </div>
          <span className="preview-type" aria-hidden="true">
            <ContentTypeIcon type={draft.contentType} />
          </span>
        </header>

        {empty ? (
          <p className="muted">Your post will appear here as you write it.</p>
        ) : (
          <>
            {first ? (
              <div className="preview-media" aria-label={draft.media.length === 1 ? 'The file on the post' : `The first of ${draft.media.length} files on the post`}>
                <MediaThumb asset={{ id: first.assetId, mediaType: first.mediaType, fileName: first.fileName }} />
                {draft.media.length > 1 ? (
                  <span className="preview-media-count">
                    1 / {draft.media.length}
                  </span>
                ) : null}
              </div>
            ) : null}
            {draft.title.trim().length > 0 ? <p className="preview-title">{draft.title.trim()}</p> : null}
            <RichTextView doc={draft.bodyDoc} />
            {draft.hashtags.length > 0 ? (
              <p className="preview-hashtags">
                {draft.hashtags.map((tag) => (
                  <span key={tag}>#{tag} </span>
                ))}
              </p>
            ) : null}
          </>
        )}

        {poll ? (
          <div className="preview-poll">
            <p>
              <strong>{poll.question || 'Poll'}</strong>
            </p>
            <ul>
              {poll.options
                .filter((option) => option.trim().length > 0)
                .map((option) => (
                  <li key={option}>{option}</li>
                ))}
            </ul>
          </div>
        ) : null}

        {draft.ctas.filter((cta) => cta.buttonText.trim().length > 0).length > 0 ? (
          <div className="preview-buttons">
            {draft.ctas
              .filter((cta) => cta.buttonText.trim().length > 0)
              .map((cta, index) => (
                <span className="preview-button" key={index}>
                  {cta.buttonText}
                </span>
              ))}
          </div>
        ) : null}

        {draft.location && draft.location.name.trim().length > 0 ? <p className="muted small">📍 {draft.location.name}</p> : null}
        {draft.mentions.length > 0 ? <p className="muted small">With {draft.mentions.map((mention) => `@${mention.name}`).join(', ')}</p> : null}
      </article>
    </section>
  );
}
