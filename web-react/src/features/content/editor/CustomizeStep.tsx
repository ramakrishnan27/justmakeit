import type { PlatformRule } from '@/api/content';
import { platformName } from '@/lib/content';
import { PlatformIcon } from '../icons';
import { textFor, titleFor, type PostDraft } from '../postDraft';
import { countedLength } from '../richText/doc';

/**
 * Step 2: for each platform the post goes to, its own text and (where the platform has one) its own title, each with a counter
 * against that platform's limit and a plain preview of what the platform will show. Left empty, a platform uses the post's own text.
 */
export function CustomizeStep({
  draft,
  update,
  rules,
  onBack,
}: {
  draft: PostDraft;
  update: (change: Partial<PostDraft>) => void;
  rules: readonly PlatformRule[];
  onBack: () => void;
}) {
  if (draft.platforms.length === 0) {
    return (
      <section className="panel" aria-labelledby="customize-empty">
        <h2 id="customize-empty">Customize for each platform</h2>
        <p className="muted">Choose at least one platform first, then come back to give each its own text.</p>
        <button className="button" type="button" onClick={onBack}>
          Choose platforms
        </button>
      </section>
    );
  }

  function change(platform: string, patch: { caption?: string; title?: string }) {
    update({ platforms: draft.platforms.map((target) => (target.platform === platform ? { ...target, ...patch } : target)) });
  }

  return (
    <div className="customize">
      <p className="muted">
        Each platform shows the post’s own text unless you write something different here. The counters include the hashtags.
      </p>
      {draft.platforms.map((target) => {
        const rule = rules.find((entry) => entry.platform === target.platform);
        const name = platformName(target.platform);
        const text = textFor(draft, target);
        const length = countedLength(text, draft.hashtags);
        const over = rule ? length - rule.captionLimit : 0;
        const title = titleFor(draft, target);
        return (
          <section className="panel platform-card" key={target.platform} aria-labelledby={`custom-${target.platform}`}>
            <div className="panel-head">
              <h2 id={`custom-${target.platform}`}>
                <PlatformIcon platform={target.platform} decorative /> {name}
              </h2>
              {rule ? (
                <p className={`counter${over > 0 ? ' is-over' : ''}`} aria-live="polite">
                  {length} / {rule.captionLimit}
                  {over > 0 ? <span> — {over} too many</span> : null}
                </p>
              ) : null}
            </div>

            <div className="field">
              <label htmlFor={`caption-${target.platform}`}>Text for {name}</label>
              <textarea
                id={`caption-${target.platform}`}
                rows={4}
                value={target.caption}
                placeholder="Uses the post’s text"
                onChange={(event) => change(target.platform, { caption: event.target.value })}
              />
              {target.caption.length > 0 ? (
                <button type="button" className="button button-quiet" onClick={() => change(target.platform, { caption: '' })}>
                  Use the post’s text
                </button>
              ) : null}
            </div>

            {rule && rule.titleLimit !== null ? (
              <div className="field">
                <label htmlFor={`title-${target.platform}`}>Title for {name}</label>
                <input
                  id={`title-${target.platform}`}
                  type="text"
                  value={target.title}
                  placeholder={draft.title.trim().length > 0 ? 'Uses the post’s title' : ''}
                  onChange={(event) => change(target.platform, { title: event.target.value })}
                />
                <p className={`hint${title.length > rule.titleLimit ? ' is-over' : ''}`}>
                  {title.length} / {rule.titleLimit}
                </p>
              </div>
            ) : null}

            <div className="plain-preview" aria-label={`What ${name} will show`}>
              <p className="muted small">What {name} will show</p>
              <pre>{[title.length > 0 && rule?.titleLimit != null ? title : null, text, draft.hashtags.length > 0 ? draft.hashtags.map((tag) => `#${tag}`).join(' ') : null].filter((part) => part !== null && part.length > 0).join('\n\n')}</pre>
            </div>
          </section>
        );
      })}
    </div>
  );
}
