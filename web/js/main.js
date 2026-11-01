// Starts the app for the address: the site admin app under /admin, the member app (sign in, create an account,
// dashboard) everywhere else. Each restores its own session from its own refresh cookie, then lets the router
// show the sign-in page or the dashboard. The token lives only in memory (see core/api.js).

import { createApiClient } from './core/api.js';
import { h } from './core/dom.js';
import { adminApp, appForPath, createRouter } from './core/router.js';
import { createSession, memberEndpoints } from './core/session.js';
import './pages/admin-dashboard.js';
import './pages/admin-login.js';
import './pages/member-dashboard.js';
import './pages/member-login.js';
import './pages/member-register.js';

const app = appForPath(window.location.pathname);
const isAdmin = app === adminApp;

const root = document.getElementById('app');
const api = createApiClient({ refreshPath: isAdmin ? '/v1/site/refresh' : '/v1/auth/refresh' });
const session = isAdmin ? createSession(api) : createSession(api, memberEndpoints);

function show(element, title) {
  root.replaceChildren(element);
  document.title = `${title} · ${app.label}`;
  // Move focus to the page heading, so a screen reader announces where the user has arrived.
  element.querySelector('h1')?.focus();
}

function render(decision) {
  if (decision.type === 'notFound') {
    show(
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

  const element = document.createElement(decision.route.element);
  element.context = { api, session };
  show(element, decision.route.title);
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
