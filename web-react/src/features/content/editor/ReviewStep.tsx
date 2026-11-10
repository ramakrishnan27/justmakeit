import { useState } from 'react';
import type { PlatformRule, PostPlatformItem } from '@/api/content';
import { contentTypeLabel, platformName } from '@/lib/content';
import { formatDate } from '@/lib/format';
import { PlatformIcon } from '../icons';
import { browserTimeZone, earliestLocalInput, fromLocalInput, toLocalInput } from '../localTime';
import { publishProblems, textFor, type PostDraft, type Problem } from '../postDraft';
import { countedLength, plainText } from '../richText/doc';
import { DeliveryStatus } from './DeliveryStatus';
import type { Step } from './Stepper';

const MINIMUM_LEAD_MS = 60 * 1000;

/**
 * Step 3: a last look at the post, what is still wrong with it (each with a way back to fix it), and when it should go out: now, or
 * at a time. Only a person who may publish gets the buttons that schedule or publish; everyone who can edit can save the draft.
 */
export function ReviewStep({
  draft,
  update,
  rules,
  deliveries,
  canPublish,
  busy,
  onStep,
  onSaveDraft,
  onSchedule,
  onPublishNow,
}: {
  draft: PostDraft;
  update: (change: Partial<PostDraft>) => void;
  rules: readonly PlatformRule[];
  deliveries: readonly PostPlatformItem[];
  canPublish: boolean;
  busy: boolean;
  onStep: (step: Step) => void;
  onSaveDraft: () => void;
  onSchedule: (at: string) => void;
  onPublishNow: () => void;
}) {
  const problems: Problem[] = publishProblems(draft, rules);
  const [mode, setMode] = useState<'now' | 'schedule'>(draft.scheduledAt ? 'schedule' : 'now');
  const text = plainText(draft.bodyDoc);
  const when = draft.scheduledAt;
  const tooSoon = when === null || new Date(when).getTime() < Date.now() + MINIMUM_LEAD_MS;
  const ready = problems.length === 0 && !busy;

  return (
    <div className="review">
      <DeliveryStatus targets={deliveries} />

      <section className="panel" aria-labelledby="review-summary">
        <h2 id="review-summary">Review</h2>
        <dl className="summary">
          <dt>Kind</dt>
          <dd>{contentTypeLabel(draft.contentType)}</dd>
          <dt>Title</dt>
          <dd>{draft.title.trim().length > 0 ? draft.title.trim() : <span className="muted">None</span>}</dd>
          <dt>Text</dt>
          <dd>{text.length > 0 ? (text.length > 200 ? `${text.slice(0, 200)}…` : text) : <span className="muted">Nothing yet</span>}</dd>
          <dt>Media</dt>
          <dd>
            {draft.media.length === 0 ? (
              <span className="muted">None</span>
            ) : (
              `${draft.media.length} ${draft.media.length === 1 ? 'file' : 'files'}: ${draft.media.map((file) => file.fileName || 'a file').join(', ')}`
            )}
          </dd>
          <dt>Hashtags</dt>
          <dd>{draft.hashtags.length > 0 ? draft.hashtags.map((tag) => `#${tag}`).join(' ') : <span className="muted">None</span>}</dd>
          <dt>Platforms</dt>
          <dd>
            {draft.platforms.length === 0 ? (
              <span className="muted">None chosen</span>
            ) : (
              <ul className="summary-platforms">
                {draft.platforms.map((target) => {
                  const rule = rules.find((entry) => entry.platform === target.platform);
                  const length = countedLength(textFor(draft, target), draft.hashtags);
                  return (
                    <li key={target.platform}>
                      <PlatformIcon platform={target.platform} decorative /> {platformName(target.platform)}
                      {rule ? <span className="muted small"> · {length} / {rule.captionLimit} characters</span> : null}
                    </li>
                  );
                })}
              </ul>
            )}
          </dd>
          <dt>Visibility</dt>
          <dd>{draft.visibility.charAt(0).toUpperCase() + draft.visibility.slice(1)}</dd>
        </dl>
      </section>

      {problems.length > 0 ? (
        <section className="panel" aria-labelledby="review-problems" role="alert">
          <h2 id="review-problems">To fix before it can go out</h2>
          <ul className="problem-list">
            {problems.map((problem) => (
              <li key={problem.message}>
                <span>{problem.message}</span>
                <button type="button" className="button button-quiet" onClick={() => onStep(problem.step)}>
                  {problem.step === 'create' ? 'Go to the post' : 'Go to Customize'}
                </button>
              </li>
            ))}
          </ul>
        </section>
      ) : null}

      <section className="panel" aria-labelledby="review-when">
        <h2 id="review-when">When should it go out?</h2>
        {canPublish ? (
          <>
            <fieldset className="field">
              <legend className="visually-hidden">When should it go out?</legend>
              <label className="choice">
                <input type="radio" name="when" checked={mode === 'now'} onChange={() => setMode('now')} />
                Publish now
              </label>
              <label className="choice">
                <input type="radio" name="when" checked={mode === 'schedule'} onChange={() => setMode('schedule')} />
                Schedule for a date and time
              </label>
            </fieldset>

            {mode === 'schedule' ? (
              <div className="field">
                <label htmlFor="review-date">Date and time</label>
                <input
                  id="review-date"
                  type="datetime-local"
                  min={earliestLocalInput(new Date(), 1)}
                  value={toLocalInput(when)}
                  onChange={(event) => update({ scheduledAt: fromLocalInput(event.target.value) })}
                  aria-describedby="review-date-hint"
                />
                <p className={`hint${when !== null && tooSoon ? ' is-over' : ''}`} id="review-date-hint">
                  {when !== null && tooSoon ? 'Choose a time at least a minute from now. ' : ''}Times are in {browserTimeZone()}.
                </p>
              </div>
            ) : null}

            <div className="review-actions">
              {mode === 'now' ? (
                <button type="button" className="button button-primary" disabled={!ready} onClick={onPublishNow}>
                  {busy ? 'Working…' : 'Publish now'}
                </button>
              ) : (
                <button type="button" className="button button-primary" disabled={!ready || tooSoon} onClick={() => when && onSchedule(when)}>
                  {busy ? 'Working…' : when ? `Schedule for ${formatDate(when, { withTime: true })}` : 'Schedule'}
                </button>
              )}
              <button type="button" className="button" disabled={busy} onClick={onSaveDraft}>
                Save as draft
              </button>
            </div>
          </>
        ) : (
          <>
            <p className="muted">Your role can write and save drafts. Someone who can publish will schedule this one.</p>
            <div className="review-actions">
              <button type="button" className="button button-primary" disabled={busy} onClick={onSaveDraft}>
                Save as draft
              </button>
            </div>
          </>
        )}
      </section>
    </div>
  );
}
