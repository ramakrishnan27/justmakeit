// Starts the site admin app: restores the session from the refresh cookie, then lets the router show
// the sign-in page or the dashboard. The token lives only in memory (see core/api.js).

import { createApiClient } from './core/api.js';
import { h } from './core/dom.js';
import { createRouter, homePath } from './core/router.js';
import { createSession } from './core/session.js';
import './pages/admin-dashboard.js';
import './pages/admin-login.js';

const app = document.getElementById('app');
const api = createApiClient();
const session = createSession(api);

function show(element, title) {
  app.replaceChildren(element);
  document.title = `${title} · Site admin`;
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
          h('a', { class: 'button button-primary button-block', href: homePath, 'data-link': true }, 'Go to the dashboard'),
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

const router = createRouter({ win: window, session, render });

// Links marked data-link move around without reloading the page.
document.addEventListener('click', (event) => {
  const link = event.target instanceof Element ? event.target.closest('a[data-link]') : null;
  if (link && event.button === 0 && !event.metaKey && !event.ctrlKey && !event.shiftKey) {
    event.preventDefault();
    router.navigate(link.getAttribute('href'));
  }
});

await session.restore();
router.start();
