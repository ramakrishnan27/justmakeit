import { h, replaceChildren } from '../core/dom.js';
import { countLabel, formatDate } from '../core/format.js';
import { messageForMemberLoadError, messageForWorkspaceError } from '../core/messages.js';
import { hasPermission, permissionKeys } from '../core/permission-keys.js';
import { allowsSeveralWorkspaces, workspaceWords } from '../core/roles.js';

/**
 * The workspaces of the person's organization: a client workspace for each client in an agency, the one workspace of
 * a creator, the workspaces of a company. The add form is shown only to someone whose access token carries the
 * workspaces.create permission, in an organization that may have several; the server checks both again.
 * The router gives it `context = { api, session }` before it is attached.
 */
class MemberWorkspaces extends HTMLElement {
  connectedCallback() {
    const { organization } = this.context.session.state;
    const words = workspaceWords(organization?.businessModel);

    this.region = h('div', { 'aria-live': 'polite' });
    this.replaceChildren(h('h1', { tabindex: '-1' }, words.many), h('p', { class: 'muted' }, `The workspaces of ${organization?.name ?? 'your organization'}.`), this.region);

    this.load();
  }

  async load() {
    const { api } = this.context;
    replaceChildren(this.region, h('p', { class: 'muted' }, 'Loading…'));

    try {
      const workspaces = await api.request('/v1/workspaces');
      replaceChildren(this.region, this.renderWorkspaces(workspaces));
    } catch (error) {
      const retry = h('button', { class: 'button', type: 'button' }, 'Try again');
      retry.addEventListener('click', () => this.load());
      replaceChildren(
        this.region,
        h('div', { class: 'notice notice-error', role: 'alert' }, h('p', {}, messageForMemberLoadError(error)), retry),
      );
    }
  }

  renderWorkspaces(workspaces) {
    const { organization, permissions } = this.context.session.state;
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

    const parts = [h('p', { class: 'muted small' }, countLabel(workspaces.length, words.one)), list];

    // Only someone who holds the permission can add one, and an individual creator has the one they started with.
    if (hasPermission(permissions, permissionKeys.workspacesCreate) && allowsSeveralWorkspaces(model)) {
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
            await this.load();
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
}

customElements.define('member-workspaces', MemberWorkspaces);
