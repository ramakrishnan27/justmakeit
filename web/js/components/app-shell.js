import { h, replaceChildren } from '../core/dom.js';
import { businessModelLabel } from '../core/format.js';
import { createIcon } from '../core/icons.js';
import { isCurrent } from '../core/menu.js';

// The same width where the stylesheet turns the drawer into a fixed column.
const desktopQuery = '(min-width: 720px)';

/**
 * The frame around every page behind sign-in: a header (the organization or the console, the person, sign out), the
 * sidebar the server built for this person, and the page. The sidebar comes from the session, which holds it in
 * memory; the shell never asks the server for it and draws again whenever the session changes.
 *
 * Narrower than 720 px the sidebar is a drawer: the menu button opens it, and Escape, the Close button, a tap outside
 * it or choosing a link closes it. While it is open the rest of the page is inert, so focus stays inside it.
 * From 720 px it is a column beside the page and the button is gone.
 *
 * main.js gives it `context = { session, app }` before it is attached, then hands it each page with `setPage`.
 */
class AppShell extends HTMLElement {
  connectedCallback() {
    const { session, app } = this.context;
    const { user, organization } = session.state;

    this.isOpen = false;

    this.toggle = h('button', { class: 'menu-toggle', type: 'button', 'aria-controls': 'shell-nav', 'aria-expanded': 'false' }, createIcon('menu'), h('span', { class: 'visually-hidden' }, 'Menu'));
    this.toggle.addEventListener('click', () => (this.isOpen ? this.closeDrawer({ focusToggle: true }) : this.openDrawer()));

    const signOut = h('button', { class: 'button', type: 'button' }, 'Sign out');
    signOut.addEventListener('click', async () => {
      signOut.disabled = true;
      await session.signOut(); // the router then moves the page to the sign-in form
    });

    const brand =
      app.name === 'admin'
        ? h('p', { class: 'brand' }, 'Platform ', h('span', { class: 'badge badge-accent' }, 'Site admin'))
        : h('p', { class: 'brand' }, organization?.name ?? 'Platform', ' ', h('span', { class: 'badge badge-model' }, businessModelLabel(organization?.businessModel)));

    this.header = h(
      'header',
      { class: 'app-header' },
      h(
        'div',
        { class: 'shell-bar' },
        this.toggle,
        brand,
        h('div', { class: 'header-actions' }, h('span', { class: 'user-email', title: user?.email ?? '' }, user?.email ?? ''), signOut),
      ),
    );

    this.closeButton = h('button', { class: 'button button-quiet shell-close', type: 'button' }, 'Close menu');
    this.closeButton.addEventListener('click', () => this.closeDrawer({ focusToggle: true }));

    this.navList = h('ul', { class: 'nav-list' });
    this.nav = h('nav', { id: 'shell-nav', class: 'shell-nav', 'aria-label': 'Main' }, this.closeButton, this.navList);
    // Choosing a link closes the drawer; the page then moves focus to its own heading.
    this.nav.addEventListener('click', (event) => {
      if (event.target instanceof Element && event.target.closest('a')) {
        this.closeDrawer();
      }
    });

    this.scrim = h('div', { class: 'shell-scrim', hidden: true });
    this.scrim.addEventListener('click', () => this.closeDrawer({ focusToggle: true }));

    this.content = h('div', { class: 'container page' });
    this.main = h('main', { id: 'main', class: 'shell-main' }, this.content);

    this.replaceChildren(this.header, h('div', { class: 'shell-body' }, this.scrim, this.nav, this.main));

    this.stopListening = session.subscribe(() => this.renderNav());
    this.desktop = window.matchMedia(desktopQuery);
    this.onLayoutChange = () => this.applyLayout();
    this.desktop.addEventListener('change', this.onLayoutChange);
    this.onKeydown = (event) => {
      if (event.key === 'Escape' && this.isOpen) {
        this.closeDrawer({ focusToggle: true });
      }
    };
    document.addEventListener('keydown', this.onKeydown);

    this.path ??= '';
    this.renderNav();
    this.applyLayout();
  }

  disconnectedCallback() {
    this.stopListening?.();
    this.desktop?.removeEventListener('change', this.onLayoutChange);
    document.removeEventListener('keydown', this.onKeydown);
  }

  /** Shows a page in the frame and marks its link in the sidebar. */
  setPage(element, path) {
    this.path = path;
    replaceChildren(this.content, element);
    this.renderNav();
    this.closeDrawer();
    window.scrollTo(0, 0);
  }

  renderNav() {
    const items = this.context.session.state.menu ?? [];
    if (items.length === 0) {
      replaceChildren(this.navList, h('li', { class: 'nav-empty muted small' }, 'No pages are available to you yet.'));
      return;
    }

    replaceChildren(
      this.navList,
      items.map((item) =>
        h(
          'li',
          {},
          h(
            'a',
            { class: 'nav-link', href: item.path, 'data-link': true, 'aria-current': isCurrent(item.path, this.path) ? 'page' : null },
            createIcon(item.icon),
            h('span', {}, item.label),
          ),
        ),
      ),
    );
  }

  openDrawer() {
    this.isOpen = true;
    this.applyLayout();
    this.closeButton.focus();
  }

  closeDrawer({ focusToggle = false } = {}) {
    if (!this.isOpen) {
      return;
    }
    this.isOpen = false;
    this.applyLayout();
    if (focusToggle) {
      this.toggle.focus();
    }
  }

  // The drawer is a drawer only on a narrow screen. On a wide one the sidebar is always there and nothing is inert.
  applyLayout() {
    const wide = this.desktop.matches;
    if (wide) {
      this.isOpen = false;
    }

    const drawerOpen = !wide && this.isOpen;
    this.nav.classList.toggle('is-open', drawerOpen);
    this.scrim.hidden = !drawerOpen;
    this.toggle.setAttribute('aria-expanded', String(drawerOpen));

    // A closed drawer is off screen and must not be reachable with Tab; an open one holds focus by making the rest inert.
    this.nav.inert = !wide && !drawerOpen;
    this.main.inert = drawerOpen;
    this.header.inert = drawerOpen;
  }
}

customElements.define('app-shell', AppShell);
