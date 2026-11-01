import { h, replaceChildren } from '../core/dom.js';
import { countLabel, formatDate, planLabel, businessModelLabel } from '../core/format.js';
import { messageForMemberLoadError, messageForWorkspaceError } from '../core/messages.js';
import { abilities, allowsSeveralWorkspaces, responsibilities, roleTitle, upcomingAreas, workspaceWords } from '../core/roles.js';

/**
 * The member dashboard. One page that changes with the person's role in their kind of organization: it says
 * what the role is for, what it may do, shows the workspaces, and lists what is still to come for that role.
 * The router gives it `context = { api, session }` before it is attached.
 */
class MemberDashboard extends HTMLElement {
  connectedCallback() {
    const { session } = this.context;
    const { user, role, organization } = session.state;
    const model = organization?.businessModel;
    const words = workspaceWords(model);
    const can = abilities(role);

    this.workspacesRegion = h('div', { 'aria-live': 'polite' });

    const signOut = h('button', { class: 'button', type: 'button' }, 'Sign out');
    signOut.addEventListener('click', async () => {
      signOut.disabled = true;
      await session.signOut(); // the router then moves the page to the sign-in form
    });

    const firstName = (user?.fullName ?? '').trim().split(/\s+/)[0] || user?.email || 'there';
    const duties = responsibilities(model, role);

    this.replaceChildren(
      h(
        'header',
        { class: 'app-header' },
        h(
          'div',
          { class: 'container header-row' },
          h('p', { class: 'brand' }, organization?.name ?? 'Platform', ' ', h('span', { class: 'badge badge-model' }, businessModelLabel(model))),
          h('div', { class: 'header-actions' }, h('span', { class: 'user-email', title: user?.email ?? '' }, user?.email ?? ''), signOut),
        ),
      ),
      h(
        'main',
        { id: 'main', class: 'container page' },
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

        h('section', { 'aria-labelledby': 'workspaces-title' }, h('h2', { id: 'workspaces-title' }, words.many), this.workspacesRegion),

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

        h('section', { 'aria-labelledby': 'session-title' }, h('h2', { id: 'session-title' }, 'Your session'), this.renderSession(organization)),
      ),
    );

    this.loadWorkspaces();
  }

  ability(text, allowed) {
    return h(
      'li',
      { class: 'ability' },
      h('span', {}, text),
      h('span', { class: `badge ${allowed ? 'badge-paid' : 'badge-muted'}` }, allowed ? 'Yes' : 'Not for your role'),
    );
  }

  async loadWorkspaces() {
    const { api } = this.context;
    replaceChildren(this.workspacesRegion, h('p', { class: 'muted' }, 'Loading…'));

    try {
      const workspaces = await api.request('/v1/workspaces');
      replaceChildren(this.workspacesRegion, this.renderWorkspaces(workspaces));
    } catch (error) {
      const retry = h('button', { class: 'button', type: 'button' }, 'Try again');
      retry.addEventListener('click', () => this.loadWorkspaces());
      replaceChildren(
        this.workspacesRegion,
        h('div', { class: 'notice notice-error', role: 'alert' }, h('p', {}, messageForMemberLoadError(error)), retry),
      );
    }
  }

  renderWorkspaces(workspaces) {
    const { role, organization } = this.context.session.state;
    const model = organization?.businessModel;
    const words = workspaceWords(model);

    const list = h(
      'ul',
      { class: 'workspace-list' },
      workspaces.map((workspace) =>
        h(
          'li',
          { class: 'card workspace' },
          h('h3', { class: 'workspace-name' }, workspace.name),
          h('p', { class: 'muted small' }, `Created ${formatDate(workspace.createdAt)}`),
        ),
      ),
    );

    const summary = h('p', { class: 'muted small' }, countLabel(workspaces.length, words.one));
    const parts = [summary, list];

    // Only an organization admin can add one, and an individual creator has the one they started with.
    if (abilities(role).manage && allowsSeveralWorkspaces(model)) {
      parts.push(this.renderAddForm(words));
    }

    return h('div', {}, parts);
  }

  renderAddForm(words) {
    const error = h('p', { class: 'form-error', id: 'workspace-error', role: 'alert', hidden: true });
    const name = h('input', {
      id: 'workspaceName',
      name: 'workspaceName',
      type: 'text',
      maxlength: '200',
      required: true,
      'aria-describedby': 'workspace-error',
    });
    const submit = h('button', { class: 'button button-primary', type: 'submit' }, `Add ${words.one}`);

    const form = h(
      'form',
      {
        class: 'add-workspace',
        novalidate: true,
        onsubmit: async (event) => {
          event.preventDefault();
          error.hidden = true;

          if (name.value.trim().length === 0) {
            error.textContent = `Enter a name for the ${words.one}.`;
            error.hidden = false;
            name.focus();
            return;
          }

          submit.disabled = true;
          try {
            await this.context.api.request('/v1/workspaces', { method: 'POST', body: { name: name.value.trim() } });
            await this.loadWorkspaces();
          } catch (failure) {
            error.textContent = messageForWorkspaceError(failure);
            error.hidden = false;
            submit.disabled = false;
          }
        },
      },
      h('div', { class: 'field' }, h('label', { for: 'workspaceName' }, `New ${words.one} name`), name),
      error,
      submit,
    );

    return h('div', { class: 'card add-workspace-card' }, h('h3', {}, `Add a ${words.one}`), form);
  }

  renderSession(organization) {
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
        h('div', {}, h('dt', {}, 'Plan'), h('dd', {}, planLabel(organization?.plan))),
        h('div', {}, h('dt', {}, 'Token expires'), h('dd', {}, expires)),
      ),
      h('p', { class: 'muted small' }, 'These come from your access token. It is kept in memory only and is renewed automatically while you work.'),
    );
  }
}

customElements.define('member-dashboard', MemberDashboard);
