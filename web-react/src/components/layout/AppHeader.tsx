import { useState, type Ref } from 'react';
import { Icon } from '@/components/icons/Icon';
import { Badge } from '@/components/ui/Badge';
import { businessModelLabel } from '@/lib/format';
import { useSessionActions, useSignedIn } from '@/session/SessionContext';

/**
 * The bar across the top: the menu button (narrow screens), who this is (the organization of a member, "Site admin" for the
 * platform account), the person and Sign out.
 */
export function AppHeader({
  drawerOpen,
  onToggleDrawer,
  toggleRef,
  inert,
}: {
  drawerOpen: boolean;
  onToggleDrawer: () => void;
  toggleRef: Ref<HTMLButtonElement>;
  /** True while the drawer is open: the header is out of reach until it is closed. */
  inert: boolean;
}) {
  const state = useSignedIn();
  const { user } = state;
  const session = useSessionActions();
  const [signingOut, setSigningOut] = useState(false);

  async function signOut() {
    setSigningOut(true);
    await session.signOut(); // the route guard then moves the page to the sign-in form
  }

  return (
    <header className="app-header" inert={inert}>
      <div className="shell-bar">
        <button
          ref={toggleRef}
          className="menu-toggle"
          type="button"
          aria-controls="shell-nav"
          aria-expanded={drawerOpen}
          onClick={onToggleDrawer}
        >
          <Icon name="menu" />
          <span className="visually-hidden">Menu</span>
        </button>
        {state.kind === 'admin' ? (
          <p className="brand">
            Platform <Badge tone="accent">Site admin</Badge>
          </p>
        ) : (
          <p className="brand">
            {state.organization.name || 'Platform'} <Badge tone="model">{businessModelLabel(state.organization.businessModel)}</Badge>
          </p>
        )}
        <div className="header-actions">
          <span className="user-email" title={user.email}>
            {user.email}
          </span>
          <button className="button" type="button" disabled={signingOut} onClick={signOut}>
            Sign out
          </button>
        </div>
      </div>
    </header>
  );
}
