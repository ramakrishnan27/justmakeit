// Starts the app for the address: the site admin app under /admin, the member app (sign in, create an account,
// the pages behind the sidebar) everywhere else. Each asks the server who its own cookie signs in, then lets the router
// show the sign-in page or a page in the app shell. The page holds no token: the sign-in is an HttpOnly cookie that
// scripts cannot read (see core/api.js).

import { createApiClient } from './core/api.js';
import { h } from './core/dom.js';
import { adminApp, appForPath, createRouter } from './core/router.js';
import { createSession, memberEndpoints } from './core/session.js';
import './components/app-shell.js';
import './pages/admin-dashboard.js';
import './pages/admin-login.js';
import './pages/admin-menus.js';
import './pages/admin-permissions.js';
import './pages/member-dashboard.js';
import './pages/member-login.js';
import './pages/member-register.js';
import './pages/member-workspaces.js';

const app = appForPath(window.location.pathname);
const isAdmin = app === adminApp;

const root = document.getElementById('app');
const api = createApiClient();
const session = isAdmin ? createSession(api) : createSession(api, memberEndpoints);

// One frame for every page behind sign-in. It is made once and reused, so the sidebar and the header are not rebuilt
// on each move between pages; it leaves the document (and stops listening to the session) when the sign-in page shows.
const shell = document.createElement('app-shell');
shell.context = { session, app };

function showTitle(title) {
  document.title = `${title} · ${app.label}`;
}

/** A page with no frame: sign in, sign up, and the not-found page of a visitor who is not signed in. */
function showBare(element, title) {
  root.replaceChildren(element);
  showTitle(title);
  // Move focus to the page heading, so a screen reader announces where the user has arrived.
  element.querySelector('h1')?.focus();
}

/** A page in the frame, with its link marked in the sidebar. */
function showInShell(element, title) {
  if (shell.parentNode !== root) {
    root.replaceChildren(shell);
  }
  shell.setPage(element, window.location.pathname);
  showTitle(title);
  element.querySelector('h1')?.focus();
}

/** A short page of words and a way home, for the places a signed-in person cannot go. */
function messagePage(title, text) {
  return h(
    'section',
    {},
    h('h1', { tabindex: '-1' }, title),
    h('p', { class: 'muted' }, text),
    h('a', { class: 'button button-primary', href: app.homePath, 'data-link': true }, 'Go to the dashboard'),
  );
}

function render(decision) {
  const signedIn = session.state.status === 'signedIn';

  if (decision.type === 'notFound') {
    if (signedIn) {
      showInShell(messagePage('Page not found', 'There is nothing at this address.'), 'Not found');
      return;
    }
    showBare(
      h(
        'main',
        { id: 'main', class: 'auth-page' },
        h(
          'section',
          { class: 'card auth-card' },
          h('h1', { tabindex: '-1' }, 'Page not found'),
          h('p', { class: 'muted' }, 'There is nothing at this address.'),
          h('a', { class: 'button button-primary button-block', href: app.homePath, 'data-link': true }, 'Go to the dashboard'),
        ),
      ),
      'Not found',
    );
    return;
  }

  // The permission the page needs is not in the person's token. The sidebar already leaves the link out; this is for
  // a typed address or an old bookmark. The server refuses the page's requests as well.
  if (decision.type === 'forbidden') {
    showInShell(
      messagePage('You do not have access to this page', 'Your role does not include the permission this page needs. If you think it should, ask an organization admin.'),
      'No access',
    );
    return;
  }

  const element = document.createElement(decision.route.element);
  element.context = { api, session };
  if (decision.route.requiresAuth) {
    showInShell(element, decision.route.title);
  } else {
    showBare(element, decision.route.title);
  }
}

// The session is settled first, and only then is the router made. A router made earlier would see the status
// change from unknown to signed in or out, and render the page once before router.start() rendered it again:
// a page that reads a one-time value from the address (the error a failed Google sign-in leaves) would lose it
// on the second render.
await session.restore();

const router = createRouter({ win: window, session, render, app });

// Links marked data-link move around without reloading the page.
document.addEventListener('click', (event) => {
  const link = event.target instanceof Element ? event.target.closest('a[data-link]') : null;
  if (link && event.button === 0 && !event.metaKey && !event.ctrlKey && !event.shiftKey) {
    event.preventDefault();
    router.navigate(link.getAttribute('href'));
  }
});

router.start();
