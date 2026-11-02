import { h, replaceChildren } from '../core/dom.js';
import { businessModelLabel, countLabel, formatDate, formatNumber, pageSummary, planLabel } from '../core/format.js';
import { messageForLoadError } from '../core/messages.js';

const pageSize = 10;

/**
 * The site admin dashboard: platform totals, a page of every organization, and the claims of the current
 * access token. The router gives it `context = { api, session }` before it is attached.
 */
class AdminDashboard extends HTMLElement {
  connectedCallback() {
    this.page = 1;
    this.overviewRegion = h('div', { class: 'stats', 'aria-live': 'polite' });
    this.agenciesRegion = h('div', { 'aria-live': 'polite' });
    this.sessionRegion = h('div');

    // The frame around the page (header, sidebar, sign out) is the app shell.
    this.replaceChildren(
      h('h1', { tabindex: '-1' }, 'Dashboard'),
      h('p', { class: 'muted' }, 'A view across every organization on the platform.'),
      h('section', { 'aria-labelledby': 'overview-title' }, h('h2', { id: 'overview-title' }, 'Overview'), this.overviewRegion),
      h('section', { 'aria-labelledby': 'agencies-title' }, h('h2', { id: 'agencies-title' }, 'Organizations'), this.agenciesRegion),
      h('section', { 'aria-labelledby': 'session-title' }, h('h2', { id: 'session-title' }, 'Your session'), this.sessionRegion),
    );

    this.load();
  }

  async load() {
    const { api } = this.context;
    replaceChildren(this.overviewRegion, h('p', { class: 'muted' }, 'Loading…'));
    replaceChildren(this.agenciesRegion, h('p', { class: 'muted' }, 'Loading…'));

    try {
      const [overview, agencies] = await Promise.all([
        api.request('/v1/site/overview'),
        api.request(`/v1/site/agencies?page=${this.page}&pageSize=${pageSize}`),
      ]);
      replaceChildren(this.overviewRegion, ...this.renderOverview(overview));
      replaceChildren(this.agenciesRegion, this.renderAgencies(agencies));
      replaceChildren(this.sessionRegion, this.renderSession());
    } catch (error) {
      const retry = h('button', { class: 'button', type: 'button' }, 'Try again');
      retry.addEventListener('click', () => this.load());
      replaceChildren(this.overviewRegion);
      replaceChildren(
        this.agenciesRegion,
        h('div', { class: 'notice notice-error', role: 'alert' }, h('p', {}, messageForLoadError(error)), retry),
      );
    }
  }

  renderOverview(overview) {
    const tile = (label, value, note) =>
      h(
        'div',
        { class: 'stat' },
        h('p', { class: 'stat-label' }, label),
        h('p', { class: 'stat-value' }, formatNumber(value)),
        note ? h('p', { class: 'stat-note' }, note) : null,
      );

    return [
      tile('Organizations', overview.agencies, `${formatNumber(overview.trialAgencies)} trial · ${formatNumber(overview.paidAgencies)} paid`),
      tile('Users', overview.users),
      tile('Workspaces', overview.workspaces),
      tile('Memberships', overview.memberships),
    ];
  }

  renderAgencies(result) {
    const refresh = h('button', { class: 'button', type: 'button' }, 'Refresh');
    refresh.addEventListener('click', () => this.load());

    if (result.items.length === 0 && result.totalCount === 0) {
      return h('div', { class: 'notice' }, h('p', {}, 'No organizations yet. They appear here as people register.'), refresh);
    }

    const rows = result.items.map((agency) =>
      h(
        'li',
        { class: 'agency' },
        h('h3', { class: 'agency-name' }, agency.name),
        h(
          'div',
          { class: 'agency-badges' },
          h('span', { class: 'badge badge-model' }, businessModelLabel(agency.businessModel)),
          h('span', { class: `badge badge-${agency.plan === 'paid' ? 'paid' : 'trial'}` }, planLabel(agency.plan)),
        ),
        h(
          'dl',
          { class: 'agency-facts' },
          h('div', {}, h('dt', {}, 'Members'), h('dd', {}, formatNumber(agency.members))),
          h('div', {}, h('dt', {}, 'Workspaces'), h('dd', {}, formatNumber(agency.workspaces))),
          h('div', {}, h('dt', {}, 'Created'), h('dd', {}, formatDate(agency.createdAt))),
        ),
      ),
    );

    const previous = h('button', { class: 'button', type: 'button', disabled: result.page <= 1 }, 'Previous');
    const next = h('button', { class: 'button', type: 'button', disabled: result.page >= result.totalPages }, 'Next');
    previous.addEventListener('click', () => this.goTo(result.page - 1));
    next.addEventListener('click', () => this.goTo(result.page + 1));

    return h(
      'div',
      {},
      h('ul', { class: 'agency-list' }, rows),
      h(
        'nav',
        { class: 'pager', 'aria-label': 'Organization pages' },
        previous,
        h('p', { class: 'pager-summary' }, pageSummary(result.page, result.pageSize, result.totalCount), ' · ', countLabel(result.totalPages, 'page')),
        next,
      ),
      h('p', { class: 'pager-refresh' }, refresh),
    );
  }

  renderSession() {
    const claims = this.context.session.getClaims() ?? {};
    const expires = typeof claims.exp === 'number' ? formatDate(claims.exp * 1000, { withTime: true }) : '–';

    return h(
      'div',
      { class: 'card' },
      h(
        'dl',
        { class: 'kv' },
        h('div', {}, h('dt', {}, 'User ID'), h('dd', { class: 'mono' }, String(claims.user_id ?? '–'))),
        h('div', {}, h('dt', {}, 'Role'), h('dd', {}, h('span', { class: 'badge badge-accent' }, String(claims.role ?? '–')))),
        h('div', {}, h('dt', {}, 'Token expires'), h('dd', {}, expires)),
      ),
      h('p', { class: 'muted small' }, 'These come from your access token. It is kept in memory only and is renewed automatically while you work.'),
    );
  }

  goTo(page) {
    this.page = Math.max(1, page);
    this.load();
  }
}

customElements.define('admin-dashboard', AdminDashboard);
