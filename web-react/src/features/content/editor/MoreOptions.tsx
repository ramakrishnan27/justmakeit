import { useState } from 'react';
import { SOCIAL_PLATFORMS, type SocialPlatform } from '@/api/content';
import { platformName } from '@/lib/content';
import { PlatformIcon } from '../icons';
import { MAX_CTAS, MAX_MENTIONS, MAX_POLL_OPTIONS, MIN_POLL_OPTIONS, type PostDraft } from '../postDraft';

type Panel = 'poll' | 'cta' | 'location' | 'people';

/**
 * "Add More Options": an emoji, a poll, call-to-action buttons, a location and the people to tag. Emoji goes to the editor's own
 * picker; the others open a small panel under the row. A panel can be closed without losing what was entered in it.
 */
export function MoreOptions({
  draft,
  update,
  onEmoji,
}: {
  draft: PostDraft;
  update: (change: Partial<PostDraft>) => void;
  onEmoji: () => void;
}) {
  const [panel, setPanel] = useState<Panel | null>(null);

  function toggle(next: Panel) {
    if (panel === next) {
      setPanel(null);
      return;
    }
    // Opening the poll or the buttons for the first time starts them with empty rows to fill in.
    if (next === 'poll' && !draft.poll) {
      update({ poll: { question: '', options: ['', ''], endsAt: null } });
    }
    if (next === 'cta' && draft.ctas.length === 0) {
      update({ ctas: [{ buttonText: '', targetUrl: '' }] });
    }
    setPanel(next);
  }

  const used = {
    poll: draft.poll !== null,
    cta: draft.ctas.length > 0,
    location: draft.location !== null && draft.location.name.trim().length > 0,
    people: draft.mentions.length > 0,
  };

  const option = (name: Panel, label: string) => (
    <button
      key={name}
      type="button"
      className={`option-button${used[name] ? ' is-used' : ''}`}
      aria-expanded={panel === name}
      aria-controls={`option-${name}`}
      onClick={() => toggle(name)}
    >
      {label}
      {used[name] ? <span className="visually-hidden"> (set)</span> : null}
    </button>
  );

  return (
    <section className="panel" aria-labelledby="panel-more">
      <h2 id="panel-more">Add More Options</h2>
      <div className="option-row">
        <button type="button" className="option-button" onClick={onEmoji}>
          Emoji
        </button>
        {option('poll', 'Poll')}
        {option('cta', 'CTA Button')}
        {option('location', 'Location')}
        {option('people', 'Tag People')}
      </div>

      {panel === 'poll' && draft.poll ? (
        <div id="option-poll" className="option-panel">
          <div className="field">
            <label htmlFor="poll-question">Poll question</label>
            <input
              id="poll-question"
              type="text"
              maxLength={300}
              value={draft.poll.question}
              onChange={(event) => update({ poll: { ...draft.poll!, question: event.target.value } })}
            />
          </div>
          {draft.poll.options.map((text, index) => (
            <div className="field" key={index}>
              <label htmlFor={`poll-option-${index}`}>Option {index + 1}</label>
              <div className="input-row">
                <input
                  id={`poll-option-${index}`}
                  type="text"
                  maxLength={100}
                  value={text}
                  onChange={(event) =>
                    update({ poll: { ...draft.poll!, options: draft.poll!.options.map((current, at) => (at === index ? event.target.value : current)) } })
                  }
                />
                {draft.poll!.options.length > MIN_POLL_OPTIONS ? (
                  <button
                    type="button"
                    className="button button-quiet"
                    aria-label={`Remove option ${index + 1}`}
                    onClick={() => update({ poll: { ...draft.poll!, options: draft.poll!.options.filter((_, at) => at !== index) } })}
                  >
                    Remove
                  </button>
                ) : null}
              </div>
            </div>
          ))}
          <div className="option-actions">
            {draft.poll.options.length < MAX_POLL_OPTIONS ? (
              <button type="button" className="button" onClick={() => update({ poll: { ...draft.poll!, options: [...draft.poll!.options, ''] } })}>
                Add option
              </button>
            ) : null}
            <button
              type="button"
              className="button button-quiet"
              onClick={() => {
                update({ poll: null });
                setPanel(null);
              }}
            >
              Remove poll
            </button>
          </div>
        </div>
      ) : null}

      {panel === 'cta' ? (
        <div id="option-cta" className="option-panel">
          {draft.ctas.map((cta, index) => (
            <fieldset className="field cta-row" key={index}>
              <legend>Button {index + 1}</legend>
              <div className="field">
                <label htmlFor={`cta-text-${index}`}>Button text</label>
                <input
                  id={`cta-text-${index}`}
                  type="text"
                  maxLength={60}
                  value={cta.buttonText}
                  onChange={(event) => update({ ctas: draft.ctas.map((current, at) => (at === index ? { ...current, buttonText: event.target.value } : current)) })}
                />
              </div>
              <div className="field">
                <label htmlFor={`cta-url-${index}`}>Web address</label>
                <input
                  id={`cta-url-${index}`}
                  type="url"
                  inputMode="url"
                  placeholder="https://example.com"
                  value={cta.targetUrl}
                  onChange={(event) => update({ ctas: draft.ctas.map((current, at) => (at === index ? { ...current, targetUrl: event.target.value } : current)) })}
                />
              </div>
              <button type="button" className="button button-quiet" onClick={() => update({ ctas: draft.ctas.filter((_, at) => at !== index) })}>
                Remove button {index + 1}
              </button>
            </fieldset>
          ))}
          {draft.ctas.length < MAX_CTAS ? (
            <button type="button" className="button" onClick={() => update({ ctas: [...draft.ctas, { buttonText: '', targetUrl: '' }] })}>
              Add button
            </button>
          ) : null}
        </div>
      ) : null}

      {panel === 'location' ? (
        <div id="option-location" className="option-panel">
          <div className="field">
            <label htmlFor="location-name">Location</label>
            <div className="input-row">
              <input
                id="location-name"
                type="text"
                maxLength={200}
                placeholder="A city, a venue or an address"
                value={draft.location?.name ?? ''}
                // Coordinates belong to the place that was named; a new name starts without them.
                onChange={(event) => update({ location: event.target.value.length === 0 ? null : { name: event.target.value, latitude: null, longitude: null } })}
              />
              {draft.location ? (
                <button type="button" className="button button-quiet" onClick={() => update({ location: null })}>
                  Remove
                </button>
              ) : null}
            </div>
          </div>
        </div>
      ) : null}

      {panel === 'people' ? <PeoplePanel draft={draft} update={update} /> : null}
    </section>
  );
}

function PeoplePanel({ draft, update }: { draft: PostDraft; update: (change: Partial<PostDraft>) => void }) {
  const [platform, setPlatform] = useState<SocialPlatform>('instagram');
  const [name, setName] = useState('');

  function add() {
    const trimmed = name.trim().replace(/^@+/, '');
    const exists = draft.mentions.some((mention) => mention.platform === platform && mention.name.toLowerCase() === trimmed.toLowerCase());
    if (trimmed.length === 0 || exists || draft.mentions.length >= MAX_MENTIONS) {
      return;
    }
    update({ mentions: [...draft.mentions, { platform, name: trimmed }] });
    setName('');
  }

  return (
    <div id="option-people" className="option-panel">
      <div className="field">
        <label htmlFor="people-name">Person or account to tag</label>
        <div className="input-row">
          <label className="visually-hidden" htmlFor="people-platform">
            Platform
          </label>
          <select id="people-platform" value={platform} onChange={(event) => setPlatform(event.target.value as SocialPlatform)}>
            {SOCIAL_PLATFORMS.map((entry) => (
              <option key={entry} value={entry}>
                {platformName(entry)}
              </option>
            ))}
          </select>
          <input
            id="people-name"
            type="text"
            maxLength={200}
            placeholder="@name"
            value={name}
            onChange={(event) => setName(event.target.value)}
            onKeyDown={(event) => {
              if (event.key === 'Enter') {
                event.preventDefault();
                add();
              }
            }}
          />
          <button type="button" className="button" onClick={add}>
            Add
          </button>
        </div>
      </div>
      {draft.mentions.length > 0 ? (
        <ul className="chips" aria-label="People tagged">
          {draft.mentions.map((mention) => (
            <li className="chip" key={`${mention.platform}:${mention.name}`}>
              <PlatformIcon platform={mention.platform} />
              <span>@{mention.name}</span>
              <button
                type="button"
                className="chip-remove"
                aria-label={`Remove @${mention.name} on ${platformName(mention.platform)}`}
                onClick={() => update({ mentions: draft.mentions.filter((current) => current !== mention) })}
              >
                ×
              </button>
            </li>
          ))}
        </ul>
      ) : null}
    </div>
  );
}
