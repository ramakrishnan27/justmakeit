import { useMemo, useRef, useState } from 'react';
import { ErrorNotice, Loading } from '@/components/ui/Notice';
import { PageTitle } from '@/components/ui/PageTitle';
import { countLabel } from '@/lib/format';
import { messageForTeamLoadError } from '@/lib/messages';
import { hasTeam } from '@/lib/roles';
import { useMember } from '@/session/SessionContext';
import { ActivateDialog } from './ActivateDialog';
import type { Feedback } from './feedback';
import { FilterBar } from './FilterBar';
import { InviteDialog } from './InviteDialog';
import { SelectionBar } from './SelectionBar';
import { TeamTable } from './TeamTable';
import { TeamToolbar } from './TeamToolbar';
import { buildRows, displayName, visibleRows, type Filter, type TeamRow } from './teamRows';
import { useTeam } from './useTeam';

/**
 * The people of the organization and the invitations still waiting, for someone whose access token carries
 * members.manage (the route checks it, and so does the server on every call). Search and filters work on what the
 * server sent; nothing is read again until something changes.
 */
export default function TeamPage() {
  const { organization } = useMember();
  const { state, reload, actions } = useTeam();
  const model = organization.businessModel;
  // An individual creator works alone: there is nobody to invite, even if someone gave the role the permission.
  const canInvite = hasTeam(model);

  const [search, setSearch] = useState('');
  const [filters, setFilters] = useState<Filter[]>([]);
  const [selected, setSelected] = useState<ReadonlySet<string>>(new Set());
  const [inviting, setInviting] = useState(false);
  // The waiting invitation being activated by hand (the dialog that asks for a default password is open).
  const [activating, setActivating] = useState<TeamRow | null>(null);
  const [feedback, setFeedback] = useState<Feedback | null>(null);
  const inviteButton = useRef<HTMLButtonElement>(null);
  const tableRegion = useRef<HTMLDivElement>(null);

  const rows = useMemo(() => (state.status === 'ready' ? buildRows(state.team) : []), [state]);
  const shown = useMemo(() => visibleRows(rows, search, filters), [rows, search, filters]);
  // Only what is on screen counts as chosen: a tick on a row the search has hidden must never be deleted by accident.
  const chosen = shown.filter((row) => selected.has(row.key) && !row.isYou);

  // The dialog gives focus back to the New Employee button itself when it closes.
  function closeInvite() {
    setInviting(false);
  }

  function activated(row: TeamRow) {
    setActivating(null);
    setFeedback({ tone: 'success', text: `${displayName(row)} is active now. They can sign in as ${row.email} with the password you set.` });
  }

  function clearSearchAndFilters() {
    setSearch('');
    setFilters([]);
  }

  const invited = rows.filter((row) => row.kind === 'invitation').length;
  const count = [countLabel(rows.length - invited, 'employee'), invited > 0 ? `${invited} invited` : null].filter((part) => part !== null).join(', ');

  return (
    <div className="team">
      <PageTitle documentTitle="Team">Team</PageTitle>
      <p className="muted">{`The people in ${organization.name || 'your organization'}, and the invitations still waiting.`}</p>

      <TeamToolbar
        search={search}
        onSearch={setSearch}
        canInvite={canInvite}
        onInvite={() => setInviting(true)}
        inviteButton={inviteButton}
      />

      {canInvite && inviting ? <InviteDialog model={model} invite={actions.invite} onClose={closeInvite} returnFocus={inviteButton} /> : null}
      {activating ? (
        <ActivateDialog
          row={activating}
          activate={actions.activate}
          onActivated={activated}
          onClose={() => setActivating(null)}
          returnFocus={tableRegion}
        />
      ) : null}
      {canInvite ? null : <p className="muted">An individual creator works alone, so there is nobody to invite.</p>}

      <div aria-live="polite">
        {state.status === 'loading' ? <Loading /> : null}

        {state.status === 'error' ? (
          <ErrorNotice message={messageForTeamLoadError(state.error)}>
            <button className="button" type="button" onClick={reload}>
              Try again
            </button>
          </ErrorNotice>
        ) : null}
      </div>

      {state.status === 'ready' ? (
        <section className="team-list" aria-labelledby="team-list-title">
          <div className="team-list-head">
            <h2 id="team-list-title">Employees</h2>
            <p className="muted small">{shown.length === rows.length ? count : `Showing ${shown.length} of ${rows.length}`}</p>
          </div>

          <FilterBar model={model} filters={filters} onChange={setFilters} />

          <div aria-live="polite">
            {feedback ? (
              <p className={`feedback feedback-${feedback.tone}`} role={feedback.tone === 'error' ? 'alert' : 'status'}>
                {feedback.text}
              </p>
            ) : null}
          </div>

          {chosen.length > 0 ? (
            <SelectionBar
              chosen={chosen}
              actions={actions}
              onFeedback={setFeedback}
              onClear={() => setSelected(new Set())}
              onDone={() => tableRegion.current?.focus()}
            />
          ) : null}

          {shown.length === 0 ? (
            <div className="team-empty">
              <p>No one matches your search or filters.</p>
              <button className="button button-pill" type="button" onClick={clearSearchAndFilters}>
                Clear search and filters
              </button>
            </div>
          ) : (
            <TeamTable
              rows={shown}
              model={model}
              selected={selected}
              onSelect={setSelected}
              actions={actions}
              onFeedback={setFeedback}
              onActivate={setActivating}
              onRemoved={() => tableRegion.current?.focus()}
              region={tableRegion}
            />
          )}

          {rows.length === 1 && rows[0]?.isYou && canInvite ? (
            <p className="muted small">You are the only one here so far. Use New Employee to invite someone.</p>
          ) : null}
        </section>
      ) : null}
    </div>
  );
}
