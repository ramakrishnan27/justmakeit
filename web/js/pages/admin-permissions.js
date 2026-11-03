import { h, replaceChildren } from '../core/dom.js';
import { cellLabel, groupSlots, isGranted, messageForMatrixLoadError, messageForPermissionError, withGrant } from '../core/permissions.js';
import { roleTitle } from '../core/roles.js';

/**
 * The Site admin's permission editor: for each permission, one checkbox for every role of every kind of organization.
 * Each tick is saved at once. The change reaches signed-in people the next time they make a request: their access token
 * is out of date, so the server refuses it and they sign in again; the new token carries the new permissions. A protected permission
 * is shown ticked and cannot be changed. The router gives it `context = { api, session }` before it is attached.
 */
class AdminPermissions extends HTMLElement {
  connectedCallback() {
    this.matrix = null;

    this.statusRegion = h('p', { class: 'form-status', role: 'status', 'aria-live': 'polite' });
    this.errorRegion = h('p', { class: 'form-error', role: 'alert', hidden: true });
    this.region = h('div');

    this.replaceChildren(
      h('h1', { tabindex: '-1' }, 'Permissions'),
      h(
        'p',
        { class: 'muted' },
        'Choose which roles hold each permission. A tick is saved at once. Any change signs everybody out: people who are signed in are asked to sign in again at their next request, and then have the new permissions.',
      ),
      this.statusRegion,
      this.errorRegion,
      this.region,
    );

    this.load();
  }

  clearMessages() {
    this.statusRegion.textContent = '';
    this.errorRegion.textContent = '';
    this.errorRegion.hidden = true;
  }

  async load() {
    replaceChildren(this.region, h('p', { class: 'muted' }, 'Loading…'));

    try {
      this.matrix = await this.context.api.request('/v1/site/permissions');
      this.render();
    } catch (error) {
      const retry = h('button', { class: 'button', type: 'button' }, 'Try again');
      retry.addEventListener('click', () => this.load());
      replaceChildren(
        this.region,
        h('div', { class: 'notice notice-error', role: 'alert' }, h('p', {}, messageForMatrixLoadError(error)), retry),
      );
    }
  }

  render() {
    const groups = groupSlots(this.matrix.slots);
    replaceChildren(
      this.region,
      h('div', { class: 'permission-list' }, this.matrix.permissions.map((permission) => this.renderPermission(permission, groups))),
    );
  }

  renderPermission(permission, groups) {
    return h(
      'fieldset',
      { class: 'card permission', 'aria-describedby': `permission-${permission.key}-about` },
      h(
        'legend',
        {},
        permission.title,
        permission.isProtected ? ' ' : null,
        permission.isProtected ? h('span', { class: 'badge badge-trial' }, 'Protected') : null,
      ),
      h(
        'div',
        { class: 'permission-about', id: `permission-${permission.key}-about` },
        h('p', { class: 'muted small' }, permission.description),
        h(
          'p',
          { class: 'muted small' },
          h('span', { class: 'mono' }, permission.key),
          permission.isProtected ? ' · Every role keeps this permission, so it cannot be changed.' : null,
        ),
      ),
      h('div', { class: 'grant-groups' }, groups.map((group) => this.renderGroup(permission, group))),
    );
  }

  renderGroup(permission, group) {
    return h(
      'div',
      { class: 'grant-group', role: 'group', 'aria-label': `${permission.title} in ${group.title}` },
      h('p', { class: 'grant-model' }, group.title),
      group.roles.map((slot) => this.renderCell(permission, group, slot)),
    );
  }

  renderCell(permission, group, slot) {
    const box = h('input', {
      type: 'checkbox',
      checked: isGranted(permission, group.businessModel, slot.role),
      disabled: permission.isProtected,
      'aria-label': cellLabel(permission, group.businessModel, slot.role),
    });
    box.addEventListener('change', () => this.toggle(permission, group, slot, box));

    return h('label', { class: 'grant' }, box, h('span', {}, slot.title));
  }

  async toggle(permission, group, slot, box) {
    const granted = box.checked;
    box.disabled = true;
    this.clearMessages();

    try {
      await this.context.api.request(`/v1/site/permissions/${encodeURIComponent(permission.key)}/grants`, {
        method: 'PUT',
        body: { businessModel: group.businessModel, role: slot.role, granted },
      });
      this.matrix = withGrant(this.matrix, permission.key, group.businessModel, slot.role, granted);
      this.statusRegion.textContent = `${permission.title}: ${granted ? 'granted to' : 'taken from'} ${roleTitle(slot.role)} (${group.title}).`;
    } catch (error) {
      box.checked = !granted; // the screen goes back to what the server still holds
      this.errorRegion.textContent = messageForPermissionError(error);
      this.errorRegion.hidden = false;
    } finally {
      box.disabled = permission.isProtected;
      box.focus();
    }
  }
}

customElements.define('admin-permissions', AdminPermissions);
