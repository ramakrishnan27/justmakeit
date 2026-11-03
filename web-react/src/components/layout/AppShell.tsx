import { useEffect, useRef, useState, type ReactNode } from 'react';
import { useLocation } from 'react-router-dom';
import { useSignedIn } from '@/session/SessionContext';
import { AppHeader } from './AppHeader';
import { Menu } from './Menu';
import { useMediaQuery } from './useMediaQuery';

// The same width where the stylesheet turns the drawer into a fixed column.
const desktopQuery = '(min-width: 720px)';

/**
 * The frame around every page behind sign-in: a header (the organization, the person, sign out), the sidebar (a
 * <Menu> of the pages the server built for this person), and the page. The menu comes from the session, which holds it
 * in memory; the shell never asks the server for it and draws again whenever the session changes. This is the only
 * place the menu is drawn, and the shell is never drawn for the sign-in and sign-up pages.
 *
 * Narrower than 720 px the sidebar is a drawer: the menu button opens it, and Escape, the Close button, a tap outside
 * it or choosing a link closes it. While it is open the rest of the page is inert, so focus stays inside it.
 * From 720 px it is a column beside the page and the button is gone.
 */
export function AppShell({ children }: { children: ReactNode }) {
  const { menu } = useSignedIn();
  const { pathname } = useLocation();
  const wide = useMediaQuery(desktopQuery);
  const [open, setOpen] = useState(false);
  const toggle = useRef<HTMLButtonElement>(null);
  const closeButton = useRef<HTMLButtonElement>(null);

  // The drawer is a drawer only on a narrow screen. On a wide one the sidebar is always there and nothing is inert.
  const drawerOpen = !wide && open;

  function closeDrawer({ focusToggle = false } = {}) {
    setOpen(false);
    if (focusToggle) {
      toggle.current?.focus();
    }
  }

  // Choosing a link (or any move to another page) closes the drawer, and the new page starts at the top.
  useEffect(() => {
    setOpen(false);
    window.scrollTo(0, 0);
  }, [pathname]);

  // A drawer left open when the screen grows must not reopen when it shrinks again.
  useEffect(() => {
    if (wide) {
      setOpen(false);
    }
  }, [wide]);

  useEffect(() => {
    if (!drawerOpen) {
      return;
    }
    closeButton.current?.focus();

    function onKeydown(event: KeyboardEvent) {
      if (event.key === 'Escape') {
        closeDrawer({ focusToggle: true });
      }
    }
    document.addEventListener('keydown', onKeydown);
    return () => document.removeEventListener('keydown', onKeydown);
  }, [drawerOpen]);

  return (
    <>
      <AppHeader
        drawerOpen={drawerOpen}
        onToggleDrawer={() => (drawerOpen ? closeDrawer({ focusToggle: true }) : setOpen(true))}
        toggleRef={toggle}
        inert={drawerOpen}
      />
      <div className="shell-body">
        <div className="shell-scrim" hidden={!drawerOpen} onClick={() => closeDrawer({ focusToggle: true })} />
        {/* A closed drawer is off screen and must not be reachable with Tab; an open one holds focus by making the rest inert. */}
        <aside id="shell-nav" className={`shell-nav${drawerOpen ? ' is-open' : ''}`} inert={!wide && !drawerOpen}>
          <button ref={closeButton} className="button button-quiet shell-close" type="button" onClick={() => closeDrawer({ focusToggle: true })}>
            Close menu
          </button>
          {/* Choosing a link closes the drawer; the page then moves focus to its own heading. */}
          <Menu label="Main" items={menu} onNavigate={() => closeDrawer()} />
        </aside>
        <main id="main" className="shell-main" inert={drawerOpen}>
          <div className="container page">{children}</div>
        </main>
      </div>
    </>
  );
}
