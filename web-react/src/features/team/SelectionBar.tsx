import { useState } from 'react';
import { countLabel } from '@/lib/format';
import { messageForTeamActionError } from '@/lib/messages';
import type { Feedback } from './feedback';
import { displayName, type TeamRow } from './teamRows';
import type { TeamActions } from './useTeam';

/** "delete 2 people and 1 invitation": what deleting these rows does, in the words of each kind of row. */
function describe(rows: readonly TeamRow[], verb: string): string {
  const people = rows.filter((row) => row.kind === 'member').length;
  const invitations = rows.length - people;
  return [
    people > 0 ? countLabel(people, 'person', 'people') : null,
    invitations > 0 ? countLabel(invitations, 'invitation') : null,
  ]
    .filter((part) => part !== null)
    .reduce((text, part, index) => (index === 0 ? `${verb} ${part}` : `${text} and ${part}`), '');
}

const capitalise = (text: string) => text.charAt(0).toUpperCase() + text.slice(1);

/**
 * What can be done to the rows that are ticked: delete them (a person loses access and their account goes; an invitation
 * stops working). It asks once, in the bar itself, and then goes through the rows one after the other.
 */
export function SelectionBar({
  chosen,
  actions,
  onFeedback,
  onClear,
  onDone,
}: {
  chosen: readonly TeamRow[];
  actions: TeamActions;
  onFeedback: (feedback: Feedback | null) => void;
  onClear: () => void;
  onDone: () => void;
}) {
  const [confirming, setConfirming] = useState(false);
  const [busy, setBusy] = useState(false);

  async function removeAll() {
    onFeedback(null);
    setBusy(true);
    const failures = await actions.removeRows(chosen);
    setBusy(false);
    setConfirming(false);

    if (failures.length === 0) {
      onFeedback({ tone: 'success', text: `${capitalise(describe(chosen, 'deleted'))}.` });
    } else {
      const [first] = failures;
      const more = failures.length > 1 ? ` ${countLabel(failures.length - 1, 'more row')} could not be changed either.` : '';
      onFeedback({
        tone: 'error',
        text: `${chosen.length - failures.length} of ${chosen.length} done. ${displayName(first!.row)}: ${messageForTeamActionError(first!.error)}${more}`,
      });
    }
    onClear();
    onDone();
  }

  return (
    <div className="selection-bar" role="region" aria-label="Selected rows">
      <span className="selection-count">{`${chosen.length} selected`}</span>
      {confirming ? (
        <span className="selection-actions" role="group" aria-label="Confirm">
          <span>{`${capitalise(describe(chosen, 'delete'))}?`}</span>
          <button className="row-action row-action-danger" type="button" disabled={busy} onClick={() => void removeAll()}>
            Yes, continue
          </button>
          <button className="row-action" type="button" disabled={busy} onClick={() => setConfirming(false)}>
            Cancel
          </button>
        </span>
      ) : (
        <span className="selection-actions">
          <button className="row-action row-action-danger" type="button" onClick={() => setConfirming(true)}>
            Delete selected
          </button>
          <button className="row-action" type="button" onClick={onClear}>
            Clear selection
          </button>
        </span>
      )}
    </div>
  );
}
