import { VISIBILITIES, type PlatformRule, type Visibility } from '@/api/content';
import type { Workspace } from '@/api/types';
import { PlatformIcon } from '../icons';
import { browserTimeZone, fromLocalInput, toLocalInput } from '../localTime';
import { togglePlatform, type PostDraft } from '../postDraft';

const visibilityLabels: Record<Visibility, string> = { public: 'Public', private: 'Private', unlisted: 'Unlisted' };

/**
 * "Post Settings": the date it is meant for, who may see it, the platforms it goes to, the campaign (none exist yet) and, for an
 * organization with several, which workspace the post belongs to (chosen when it is made, and fixed after). Only the platforms the
 * Site admin enabled for the organization are offered.
 */
export function PostSettings({
  draft,
  update,
  rules,
  workspaces,
  workspaceWord,
  workspaceLocked,
  campaignsAvailable,
  problem,
}: {
  draft: PostDraft;
  update: (change: Partial<PostDraft>) => void;
  rules: readonly PlatformRule[];
  workspaces: readonly Pick<Workspace, 'id' | 'name'>[];
  /** "product" or "client". */
  workspaceWord: string;
  workspaceLocked: boolean;
  campaignsAvailable: boolean;
  /** What the server said about the platforms, if it refused them. */
  problem?: string | null;
}) {
  const enabled = rules.filter((rule) => rule.enabled);
  const chosen = new Set(draft.platforms.map((target) => target.platform));
  const workspace = workspaces.find((entry) => entry.id === draft.workspaceId);

  return (
    <section className="panel" aria-labelledby="panel-settings">
      <h2 id="panel-settings">Post Settings</h2>

      {workspaces.length > 1 ? (
        <div className="field">
          <label htmlFor="setting-workspace">{workspaceWord.charAt(0).toUpperCase() + workspaceWord.slice(1)}</label>
          {workspaceLocked ? (
            <p id="setting-workspace">{workspace?.name ?? '–'}</p>
          ) : (
            <select id="setting-workspace" value={draft.workspaceId} onChange={(event) => update({ workspaceId: event.target.value })}>
              {workspaces.map((entry) => (
                <option key={entry.id} value={entry.id}>
                  {entry.name}
                </option>
              ))}
            </select>
          )}
        </div>
      ) : null}

      <div className="field">
        <label htmlFor="setting-date">Publish Date</label>
        <div className="input-row">
          <input
            id="setting-date"
            type="datetime-local"
            value={toLocalInput(draft.scheduledAt)}
            onChange={(event) => update({ scheduledAt: fromLocalInput(event.target.value) })}
            aria-describedby="setting-date-hint"
          />
          {draft.scheduledAt ? (
            <button type="button" className="button button-quiet" onClick={() => update({ scheduledAt: null })}>
              Clear
            </button>
          ) : null}
        </div>
        <p className="hint" id="setting-date-hint">
          Times are in {browserTimeZone()}. The post goes out only when you schedule it.
        </p>
      </div>

      <div className="field">
        <label htmlFor="setting-visibility">Visibility</label>
        <select id="setting-visibility" value={draft.visibility} onChange={(event) => update({ visibility: event.target.value as Visibility })}>
          {VISIBILITIES.map((visibility) => (
            <option key={visibility} value={visibility}>
              {visibilityLabels[visibility]}
            </option>
          ))}
        </select>
      </div>

      <fieldset className="field platform-field">
        <legend>Platforms</legend>
        {enabled.length === 0 ? (
          <p className="muted small">No platform is enabled for your organization yet. The platform administrator turns them on.</p>
        ) : (
          <div className="platform-choices">
            {enabled.map((rule) => (
              <button
                key={rule.platform}
                type="button"
                className="platform-choice"
                aria-pressed={chosen.has(rule.platform)}
                onClick={() => update({ platforms: togglePlatform(draft.platforms, rule.platform) })}
              >
                <PlatformIcon platform={rule.platform} decorative />
                <span>{rule.name}</span>
              </button>
            ))}
          </div>
        )}
        {/* A platform the draft already has but that is no longer enabled stays visible, so it can be taken away. */}
        {draft.platforms
          .filter((target) => !enabled.some((rule) => rule.platform === target.platform))
          .map((target) => (
            <p className="form-error" key={target.platform}>
              {rules.find((rule) => rule.platform === target.platform)?.name ?? target.platform} is no longer enabled for your organization.{' '}
              <button type="button" className="button button-quiet" onClick={() => update({ platforms: togglePlatform(draft.platforms, target.platform) })}>
                Remove it
              </button>
            </p>
          ))}
        {problem ? (
          <p className="form-error" role="alert">
            {problem}
          </p>
        ) : null}
      </fieldset>

      <div className="field">
        <label htmlFor="setting-campaign">
          Campaign <span className="muted">(Optional)</span>
        </label>
        <select id="setting-campaign" disabled={!campaignsAvailable} defaultValue="" title={campaignsAvailable ? undefined : 'Campaigns are not available yet'}>
          <option value="">{campaignsAvailable ? 'Select Campaign' : 'No campaigns yet'}</option>
        </select>
      </div>
    </section>
  );
}
