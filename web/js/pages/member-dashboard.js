import { h } from '../core/dom.js';
import { formatDate, planLabel } from '../core/format.js';
import { abilities, responsibilities, roleTitle, upcomingAreas, workspaceWords } from '../core/roles.js';

/**
 * The member dashboard. One page that changes with the person's role in their kind of organization: it says
 * what the role is for, what it may do, and lists what is still to come for that role. The frame around it (header,
 * sidebar) is the app shell; the workspaces have a page of their own. The router gives it `context = { api, session }`
 * before it is attached.
 */
class MemberDashboard extends HTMLElement {
  connectedCallback() {
    const { session } = this.context;
    const { user, role, organization, permissions } = session.state;
    const model = organization?.businessModel;
    const words = workspaceWords(model);
    const can = abilities(permissions);

    const firstName = (user?.fullName ?? '').trim().split(/\s+/)[0] || user?.email || 'there';
    const duties = responsibilities(model, role);

    this.replaceChildren(
      h('h1', { tabindex: '-1' }, `Welcome, ${firstName}`),
      h('p', { class: 'muted' }, `${roleTitle(role)} at ${organization?.name ?? 'your organization'}`),

      h(
        'section',
        { 'aria-labelledby': 'role-title' },
        h('h2', { id: 'role-title' }, 'Your role'),
        h(
          'div',
          { class: 'card role-card', 'data-role': role },
          h('p', {}, h('span', { class: 'badge badge-accent' }, roleTitle(role))),
          h('p', {}, duties ?? 'Your role gives you access to this organization.'),
        ),
      ),

      h(
        'section',
        { 'aria-labelledby': 'abilities-title' },
        h('h2', { id: 'abilities-title' }, 'What you can do'),
        h(
          'ul',
          { class: 'ability-list' },
          this.ability('View the workspaces and everything in them', can.read),
          this.ability('Create and change content', can.write),
          this.ability(`Add ${words.one}s and manage the team`, can.manage),
        ),
      ),

      h(
        'section',
        { 'aria-labelledby': 'soon-title' },
        h('h2', { id: 'soon-title' }, 'Coming soon for your role'),
        h(
          'ul',
          { class: 'soon-list' },
          upcomingAreas(role).map((area) =>
            h(
              'li',
              { class: 'card soon-item' },
              h('h3', {}, area.title, ' ', h('span', { class: 'badge badge-trial' }, 'Coming soon')),
              h('p', { class: 'muted small' }, area.text),
            ),
          ),
        ),
      ),

      h('section', { 'aria-labelledby': 'session-title' }, h('h2', { id: 'session-title' }, 'Your session'), this.renderSession(organization, permissions)),
    );
  }

  ability(text, allowed) {
    return h(
      'li',
      { class: 'ability' },
      h('span', {}, text),
      h('span', { class: `badge ${allowed ? 'badge-paid' : 'badge-muted'}` }, allowed ? 'Yes' : 'Not for your role'),
    );
  }

  renderSession(organization, permissions) {
    const { user, role, expiresAt } = this.context.session.state;
    const held = Array.isArray(permissions) ? permissions : [];

    return h(
      'div',
      { class: 'card' },
      h(
        'dl',
        { class: 'kv' },
        h('div', {}, h('dt', {}, 'User ID'), h('dd', { class: 'mono' }, String(user?.id ?? '–'))),
        h('div', {}, h('dt', {}, 'Role'), h('dd', {}, h('span', { class: 'badge badge-accent' }, String(role ?? '–')))),
        h('div', {}, h('dt', {}, 'Plan'), h('dd', {}, planLabel(organization?.plan))),
        h('div', {}, h('dt', {}, 'Signed in until'), h('dd', {}, formatDate(expiresAt, { withTime: true }))),
      ),
      h(
        'div',
        { class: 'permission-chips' },
        h('p', { class: 'small muted' }, 'Your permissions'),
        h('ul', { class: 'chip-list' }, held.map((permission) => h('li', {}, h('span', { class: 'badge badge-muted mono' }, permission)))),
      ),
      h('p', { class: 'muted small' }, 'Your sign-in is kept in a cookie this page cannot read. It is not renewed: when it ends, or when your permissions change, you sign in again.'),
    );
  }
}

customElements.define('member-dashboard', MemberDashboard);
