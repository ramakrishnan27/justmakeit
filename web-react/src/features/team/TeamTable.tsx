import { useEffect, useRef, useState, type Ref } from 'react';
import type { BusinessModel, OrganizationRole } from '@/api/types';
import { Badge, type BadgeTone } from '@/components/ui/Badge';
import { messageForTeamActionError } from '@/lib/messages';
import { roleTitle, rolesFor } from '@/lib/roles';
import type { Feedback } from './feedback';
import { createdText, displayName, expiryText, statusTitle, type RowStatus, type TeamRow } from './teamRows';
import type { TeamActions } from './useTeam';

const statusTones: Record<RowStatus, BadgeTone> = { active: 'paid', inactive: 'muted', invited: 'trial', expired: 'danger' };

type Confirming = 'delete' | 'deactivate' | null;

interface RowProps {
  row: TeamRow;
  model: BusinessModel;
  checked: boolean;
  onToggle: (row: TeamRow) => void;
  actions: TeamActions;
  onFeedback: (feedback: Feedback | null) => void;
  /** Opens the dialog that asks for a default password: an invited person is activated by hand. */
  onActivate: (row: TeamRow) => void;
  /** Called after a row went away, so the page can put keyboard focus somewhere that still exists. */
  onRemoved: () => void;
}

function TeamTableRow({ row, model, checked, onToggle, actions, onFeedback, onActivate, onRemoved }: RowProps) {
  const [busy, setBusy] = useState(false);
  const [confirming, setConfirming] = useState<Confirming>(null);
  const [pendingRole, setPendingRole] = useState<OrganizationRole | null>(null);
  const name = displayName(row);
  const roles = rolesFor(model);
  // A role this organization no longer allows is still shown, so the select never claims a role the person does not have.
  const options = roles.includes(row.role) ? roles : [row.role, ...roles];

  async function changeRole(role: OrganizationRole) {
    onFeedback(null);
    setPendingRole(role);
    setBusy(true);
    try {
      await actions.changeRole(row.id, role);
      onFeedback({ tone: 'success', text: `${name} is now ${roleTitle(role)}. They will be asked to sign in again.` });
    } catch (error) {
      onFeedback({ tone: 'error', text: messageForTeamActionError(error) });
    } finally {
      setPendingRole(null);
      setBusy(false);
    }
  }

  async function setActive(isActive: boolean) {
    onFeedback(null);
    setBusy(true);
    try {
      await actions.setActive(row.id, isActive);
      onFeedback({
        tone: 'success',
        text: isActive ? `${name} is active again and can sign in.` : `${name} was deactivated and signed out. They cannot sign in until you activate them.`,
      });
    } catch (error) {
      onFeedback({ tone: 'error', text: messageForTeamActionError(error) });
    } finally {
      setBusy(false);
      setConfirming(null);
    }
  }

  async function remove() {
    onFeedback(null);
    setBusy(true);
    const [failure] = await actions.removeRows([row]);
    setBusy(false);
    setConfirming(null);
    if (failure) {
      onFeedback({ tone: 'error', text: messageForTeamActionError(failure.error) });
      return;
    }
    onFeedback({ tone: 'success', text: row.kind === 'member' ? `${name} was deleted.` : `The invitation for ${row.email} was deleted.` });
    onRemoved();
  }

  const expiry = expiryText(row);

  return (
    <tr role="row" className={checked ? 'is-selected' : undefined}>
      <td role="cell" className="cell-check">
        <label className="check">
          <input
            type="checkbox"
            aria-label={`Select ${name}`}
            checked={checked}
            disabled={row.isYou || busy}
            title={row.isYou ? 'You cannot change or delete yourself.' : undefined}
            onChange={() => onToggle(row)}
          />
        </label>
      </td>

      <th role="rowheader" scope="row" className="cell-name">
        <span className="employee-name">
          {displayName(row)}
          {row.isYou ? <Badge tone="accent">You</Badge> : null}
        </span>
        {row.name.trim().length > 0 ? <span className="employee-email">{row.email}</span> : null}
      </th>

      <td role="cell" className="cell-role">
        {row.kind === 'member' && !row.isYou ? (
          <select
            aria-label={`Role of ${name}`}
            value={pendingRole ?? row.role}
            disabled={busy}
            onChange={(event) => void changeRole(event.target.value as OrganizationRole)}
          >
            {options.map((role) => (
              <option key={role} value={role}>
                {roleTitle(role)}
              </option>
            ))}
          </select>
        ) : (
          <span className="role-text">{roleTitle(row.role)}</span>
        )}
      </td>

      <td role="cell" className="cell-status">
        <Badge tone={statusTones[row.status]}>{statusTitle(row.status)}</Badge>
        {expiry ? <span className="status-date">{expiry}</span> : null}
      </td>

      <td role="cell" className="cell-created">
        <span className="created-label">Created </span>
        {createdText(row)}
      </td>

      <td role="cell" className="cell-actions">
        {row.isYou ? null : confirming ? (
          <span className="row-confirm" role="group" aria-label={`Confirm: ${confirming} ${name}`}>
            <span>{confirming === 'delete' ? 'Delete?' : 'Deactivate?'}</span>
            <button
              className="row-action row-action-danger"
              type="button"
              disabled={busy}
              aria-label={`Yes, ${confirming} ${name}`}
              onClick={() => void (confirming === 'delete' ? remove() : setActive(false))}
            >
              {confirming === 'delete' ? 'Yes, delete' : 'Yes, deactivate'}
            </button>
            <button className="row-action" type="button" disabled={busy} onClick={() => setConfirming(null)}>
              Cancel
            </button>
          </span>
        ) : (
          <span className="row-actions">
            {row.status === 'active' ? (
              <button className="row-action" type="button" disabled={busy} aria-label={`Deactivate ${name}`} onClick={() => setConfirming('deactivate')}>
                Deactivate
              </button>
            ) : null}
            {row.status === 'inactive' ? (
              <button className="row-action" type="button" disabled={busy} aria-label={`Activate ${name}`} onClick={() => void setActive(true)}>
                Activate
              </button>
            ) : null}
            {row.kind === 'invitation' ? (
              <button className="row-action" type="button" disabled={busy} aria-label={`Activate ${name}`} onClick={() => onActivate(row)}>
                Activate
              </button>
            ) : null}
            <button className="row-action" type="button" disabled={busy} aria-label={`Delete ${name}`} onClick={() => setConfirming('delete')}>
              Delete
            </button>
          </span>
        )}
      </td>
    </tr>
  );
}

/**
 * The people and the waiting invitations, as the table in the design: a tick box for each row (and one for all), the
 * person with their address, their role (changed right here), their status, the date they were created, and what can
 * be done to the row: activate or deactivate, delete. Below 76 rem each row becomes a small card, so nothing needs
 * sideways scrolling to reach a role or a button.
 */
export function TeamTable({
  rows,
  model,
  selected,
  onSelect,
  actions,
  onFeedback,
  onActivate,
  onRemoved,
  region,
}: {
  rows: readonly TeamRow[];
  model: BusinessModel;
  selected: ReadonlySet<string>;
  onSelect: (next: Set<string>) => void;
  actions: TeamActions;
  onFeedback: (feedback: Feedback | null) => void;
  onActivate: (row: TeamRow) => void;
  onRemoved: () => void;
  region: Ref<HTMLDivElement>;
}) {
  const selectAll = useRef<HTMLInputElement>(null);
  const selectable = rows.filter((row) => !row.isYou);
  const chosen = selectable.filter((row) => selected.has(row.key));
  const all = selectable.length > 0 && chosen.length === selectable.length;

  // "Some but not all" has no attribute: it is a property that only a script can set.
  useEffect(() => {
    if (selectAll.current) {
      selectAll.current.indeterminate = chosen.length > 0 && !all;
    }
  }, [chosen.length, all]);

  function toggle(row: TeamRow) {
    const next = new Set(selected);
    if (!next.delete(row.key)) {
      next.add(row.key);
    }
    onSelect(next);
  }

  function toggleAll() {
    const next = new Set(selected);
    for (const row of selectable) {
      if (all) {
        next.delete(row.key);
      } else {
        next.add(row.key);
      }
    }
    onSelect(next);
  }

  return (
    <div className="table-scroll" ref={region} tabIndex={-1}>
      <table className="team-table" role="table">
        <caption className="visually-hidden">The people in your organization and the invitations still waiting</caption>
        <thead role="rowgroup">
          <tr role="row">
            <th role="columnheader" scope="col" className="cell-check">
              <label className="check">
                <input type="checkbox" ref={selectAll} aria-label="Select all" checked={all} disabled={selectable.length === 0} onChange={toggleAll} />
              </label>
              <span className="select-all-text" aria-hidden="true">
                Select all
              </span>
            </th>
            <th role="columnheader" scope="col" className="cell-name">
              Employee
            </th>
            <th role="columnheader" scope="col" className="cell-role">
              Role
            </th>
            <th role="columnheader" scope="col" className="cell-status">
              Status
            </th>
            <th role="columnheader" scope="col" className="cell-created">
              Created
            </th>
            <th role="columnheader" scope="col" className="cell-actions">
              <span className="visually-hidden">Actions</span>
            </th>
          </tr>
        </thead>
        <tbody role="rowgroup">
          {rows.map((row) => (
            <TeamTableRow
              key={row.key}
              row={row}
              model={model}
              checked={selected.has(row.key)}
              onToggle={toggle}
              actions={actions}
              onFeedback={onFeedback}
              onActivate={onActivate}
              onRemoved={onRemoved}
            />
          ))}
        </tbody>
      </table>
    </div>
  );
}
