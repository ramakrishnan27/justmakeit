import { useCallback, useEffect, useId, useRef, useState } from 'react';
import type { SiteOrganization } from '@/api/types';
import { UiIcon } from '@/components/icons/UiIcon';
import { Badge } from '@/components/ui/Badge';
import { FeedbackLine, type Feedback } from '@/components/ui/Feedback';
import { businessModelLabel, formatDate, formatNumber, planLabel } from '@/lib/format';
import { messageForOrganizationStatusError, statusChangedText } from '@/lib/organizationAdmin';
import { useApi } from '@/session/SessionContext';
import { OrganizationChannelsPanel } from './OrganizationChannelsPanel';

type Confirming = 'deactivate' | null;

function OrganizationManageCard({ organization }: { organization: SiteOrganization }) {
  const api = useApi();
  const panelId = useId();
  // The button that is the row's action right now: Deactivate, Activate, or (while asking) Cancel, the safe answer.
  const action = useRef<HTMLButtonElement>(null);
  const [open, setOpen] = useState(false);
  // The panel is drawn the first time the card opens and stays drawn (hidden) after, so what it read is not read again.
  const [opened, setOpened] = useState(false);
  const [active, setActive] = useState(organization.isActive);
  const [channels, setChannels] = useState(organization.channels);
  const [confirming, setConfirming] = useState<Confirming>(null);
  const [busy, setBusy] = useState(false);
  const [feedback, setFeedback] = useState<Feedback | null>(null);
  const name = organization.name;

  // The buttons in this row change place when a question is asked or answered; keyboard focus follows the action. Compared with
  // what it was, so drawing the card for the first time (or drawing it twice, as development does) moves nothing.
  const before = useRef({ confirming, active });
  useEffect(() => {
    if (before.current.confirming !== confirming || before.current.active !== active) {
      before.current = { confirming, active };
      action.current?.focus();
    }
  }, [confirming, active]);

  const showCount = useCallback((count: number) => setChannels(count), []);

  function toggle() {
    setOpen((current) => !current);
    setOpened(true);
  }

  async function setStatus(next: boolean) {
    setFeedback(null);
    setBusy(true);
    try {
      await api.request(`/v1/site/agencies/${encodeURIComponent(organization.id)}/status`, { method: 'PUT', body: { isActive: next } });
      setActive(next);
      setFeedback({ tone: 'success', text: statusChangedText(name, next) });
    } catch (error) {
      setFeedback({ tone: 'error', text: messageForOrganizationStatusError(error, name, next) });
    } finally {
      setBusy(false);
      setConfirming(null);
    }
  }

  return (
    <li className={`agency org-card${active ? '' : ' is-inactive'}`}>
      <h3 className="agency-name">
        {/* The whole card is the click target (see .org-toggle::after); this button is what the keyboard and a screen reader use. */}
        <button className="org-toggle" type="button" aria-expanded={open} aria-controls={panelId} onClick={toggle}>
          <span>{name}</span>
          <UiIcon name="chevron" />
        </button>
      </h3>
      <div className="agency-badges">
        <Badge tone="model">{businessModelLabel(organization.businessModel)}</Badge>
        <Badge tone={organization.plan === 'paid' ? 'paid' : 'trial'}>{planLabel(organization.plan)}</Badge>
        {active ? null : <Badge tone="danger">Inactive</Badge>}
      </div>
      <dl className="agency-facts agency-facts-wide">
        <div>
          <dt>Members</dt>
          <dd>{formatNumber(organization.members)}</dd>
        </div>
        <div>
          <dt>Workspaces</dt>
          <dd>{formatNumber(organization.workspaces)}</dd>
        </div>
        <div>
          <dt>Channels</dt>
          <dd>{formatNumber(channels)}</dd>
        </div>
        <div>
          <dt>Created</dt>
          <dd>{formatDate(organization.createdAt)}</dd>
        </div>
      </dl>

      <div className="org-actions">
        {confirming ? (
          <span className="row-confirm" role="group" aria-label={`Confirm: deactivate ${name}`}>
            <span>Deactivate? Its members are signed out at once.</span>
            <button
              className="row-action row-action-danger"
              type="button"
              disabled={busy}
              aria-label={`Yes, deactivate ${name}`}
              onClick={() => void setStatus(false)}
            >
              Yes, deactivate
            </button>
            <button ref={action} className="row-action" type="button" disabled={busy} onClick={() => setConfirming(null)}>
              Cancel
            </button>
          </span>
        ) : active ? (
          <button ref={action} className="row-action" type="button" disabled={busy} aria-label={`Deactivate ${name}`} onClick={() => setConfirming('deactivate')}>
            Deactivate
          </button>
        ) : (
          <button ref={action} className="row-action" type="button" disabled={busy} aria-label={`Activate ${name}`} onClick={() => void setStatus(true)}>
            Activate
          </button>
        )}
      </div>

      <FeedbackLine feedback={feedback} />

      {opened ? (
        <div id={panelId} className="org-panel" role="region" aria-label={`Channels of ${name}`} hidden={!open}>
          <OrganizationChannelsPanel organizationId={organization.id} organizationName={name} onEnabledCount={showCount} />
        </div>
      ) : null}
    </li>
  );
}

/**
 * One card for each organization, as in the dashboard's list, that the Site admin can act on. Clicking a card (or pressing its
 * name) opens a section with the channels, each with a switch; Deactivate (asked twice, because it signs everybody in the
 * organization out) and Activate sit on the card. The dashboard's own list stays read-only: this is where it is done.
 */
export function OrganizationManageList({ organizations }: { organizations: readonly SiteOrganization[] }) {
  return (
    <ul className="agency-list">
      {organizations.map((organization) => (
        <OrganizationManageCard key={organization.id} organization={organization} />
      ))}
    </ul>
  );
}
