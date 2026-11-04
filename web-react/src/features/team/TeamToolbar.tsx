import type { Ref } from 'react';
import { UiIcon } from '@/components/icons/UiIcon';

/**
 * The top of the team page: the search box, and the button that opens the invite dialog. The button is drawn only when
 * the organization can have a team and the person may manage it (the page itself already needs members.manage).
 */
export function TeamToolbar({
  search,
  onSearch,
  canInvite,
  onInvite,
  inviteButton,
}: {
  search: string;
  onSearch: (value: string) => void;
  /** False when there is nobody to invite (an individual creator works alone). */
  canInvite: boolean;
  /** Opens the invite dialog. */
  onInvite: () => void;
  inviteButton: Ref<HTMLButtonElement>;
}) {
  return (
    <div className="team-toolbar">
      <div className="search">
        <span className="search-icon">
          <UiIcon name="search" />
        </span>
        <input
          type="search"
          name="search"
          aria-label="Search employees"
          placeholder="Search by name, email or role"
          autoComplete="off"
          maxLength={100}
          value={search}
          onChange={(event) => onSearch(event.target.value)}
        />
      </div>

      {canInvite ? (
        <button
          className="button button-primary button-pill"
          type="button"
          ref={inviteButton}
          aria-haspopup="dialog"
          onClick={onInvite}
        >
          <UiIcon name="plus" />
          New Employee
        </button>
      ) : null}
    </div>
  );
}
