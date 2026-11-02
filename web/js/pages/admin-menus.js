import { h, replaceChildren } from '../core/dom.js';
import { createIcon, iconLabel } from '../core/icons.js';
import {
  canMove,
  defaultLabelFor,
  maxLabelLength,
  menus,
  messageForMenuError,
  messageForMenuLoadError,
  moveItem,
  shouldReloadAfter,
  validateMenuItem,
} from '../core/menu-admin.js';

/**
 * The Site admin's menu editor. It lists one menu at a time (the members' sidebar or this console's own) with every
 * item, hidden ones too, and lets the admin move, rename, re-icon, hide, delete and add. Everything that changes the
 * order is a button, never only a drag. The server enforces the rules (a locked item cannot be hidden or deleted); the
 * buttons only show what is possible. The router gives it `context = { api, session }` before it is attached.
 */
class AdminMenus extends HTMLElement {
  connectedCallback() {
    this.menuKey = menus[0].key;
    this.config = null;
    this.editing = null;
    this.draft = null;
    this.confirming = null;
    this.busy = false;

    this.statusRegion = h('p', { class: 'form-status', role: 'status', 'aria-live': 'polite' });
    this.errorRegion = h('p', { class: 'form-error', role: 'alert', hidden: true });
    this.description = h('p', { class: 'muted small' }, menus[0].description);
    this.listHeading = h('h2', { id: 'menu-items-title', tabindex: '-1' }, 'Items');
    this.listRegion = h('div');
    this.addRegion = h('div');

    const choice = h(
      'select',
      { id: 'menu-choice', name: 'menu' },
      menus.map((menu) => h('option', { value: menu.key }, menu.title)),
    );
    this.choice = choice;
    choice.addEventListener('change', () => {
      this.menuKey = choice.value;
      this.description.textContent = menus.find((menu) => menu.key === this.menuKey)?.description ?? '';
      this.editing = null;
      this.draft = null;
      this.confirming = null;
      this.clearMessages();
      this.load();
    });

    this.replaceChildren(
      h('h1', { tabindex: '-1' }, 'Menus'),
      h('p', { class: 'muted' }, 'Choose which pages each sidebar shows, in which order and under which words. Who sees an item is decided by the permissions on the next page.'),
      h('div', { class: 'field' }, h('label', { for: 'menu-choice' }, 'Menu'), choice, this.description),
      this.statusRegion,
      this.errorRegion,
      h('section', { 'aria-labelledby': 'menu-items-title' }, this.listHeading, this.listRegion),
      h('section', { 'aria-labelledby': 'menu-add-title' }, h('h2', { id: 'menu-add-title' }, 'Add a page'), this.addRegion),
    );

    this.load();
  }

  // --- Reading ------------------------------------------------------------------------------

  async fetchConfig() {
    this.config = await this.context.api.request(`/v1/site/menus/${this.menuKey}`);
  }

  async load() {
    replaceChildren(this.listRegion, h('p', { class: 'muted' }, 'Loading…'));
    replaceChildren(this.addRegion);

    try {
      await this.fetchConfig();
      this.renderList();
      this.renderAdd();
    } catch (error) {
      const retry = h('button', { class: 'button', type: 'button' }, 'Try again');
      retry.addEventListener('click', () => this.load());
      replaceChildren(
        this.listRegion,
        h('div', { class: 'notice notice-error', role: 'alert' }, h('p', {}, messageForMenuLoadError(error)), retry),
      );
    }
  }

  // --- Messages and the one action at a time ------------------------------------------------

  clearMessages() {
    this.statusRegion.textContent = '';
    this.errorRegion.textContent = '';
    this.errorRegion.hidden = true;
  }

  say(text) {
    this.clearMessages();
    this.statusRegion.textContent = text;
  }

  fail(error) {
    this.clearMessages();
    this.errorRegion.textContent = messageForMenuError(error);
    this.errorRegion.hidden = false;
  }

  /**
   * Runs one change. While it runs every button is off, so two changes can never cross; afterwards the list is drawn
   * again from what the server answered, and focus goes where the person was working.
   */
  async run(task, { success, focus } = {}) {
    if (this.busy) {
      return;
    }
    this.busy = true;
    this.listRegion.setAttribute('aria-busy', 'true');
    this.addFieldset?.setAttribute('disabled', '');
    this.choice.disabled = true; // an answer for one menu must never land on the other's list
    this.renderList();

    try {
      const outcome = await task();
      if (success) {
        this.say(typeof success === 'function' ? success(outcome) : success);
      }
      // This console's own sidebar follows an edit of its own menu.
      if (this.menuKey === 'admin') {
        void this.context.session.refreshMenu();
      }
    } catch (error) {
      this.fail(error);
      if (shouldReloadAfter(error)) {
        try {
          await this.fetchConfig();
          this.renderAdd();
        } catch {
          // The list on screen stays; the message above already says what went wrong.
        }
      }
    } finally {
      this.busy = false;
      this.listRegion.removeAttribute('aria-busy');
      this.addFieldset?.removeAttribute('disabled');
      this.choice.disabled = false;
      this.renderList();
      this.focusAfter(focus);
    }
  }

  focusAfter(focus) {
    if (typeof focus === 'function') {
      focus(this)?.focus();
    }
  }

  async send(path, method, body) {
    return this.context.api.request(path, { method, body });
  }

  itemPath(item) {
    return `/v1/site/menus/${this.menuKey}/items/${item.id}`;
  }

  button(text, { label, action, item, disabled = false, onClick }) {
    const button = h(
      'button',
      {
        class: 'button button-small',
        type: 'button',
        'aria-label': label,
        'data-action': action,
        'data-item': item?.id,
        disabled: disabled || this.busy,
      },
      text,
    );
    button.addEventListener('click', onClick);
    return button;
  }

  findButton(id, action) {
    return [...this.querySelectorAll('button[data-item]')].find((button) => button.dataset.item === id && button.dataset.action === action) ?? null;
  }

  // --- The list -----------------------------------------------------------------------------

  renderList() {
    const items = this.config?.items ?? [];
    const ids = items.map((item) => item.id);

    replaceChildren(
      this.listRegion,
      h('p', { class: 'muted small' }, `${items.length} ${items.length === 1 ? 'item' : 'items'}. Hidden items stay here so you can show them again.`),
      h('ul', { class: 'menu-items' }, items.map((item) => (this.editing === item.id ? this.renderEdit(item) : this.renderItem(item, ids)))),
    );
  }

  renderItem(item, ids) {
    const confirming = this.confirming === item.id;

    const facts = [h('span', { class: 'mono' }, item.path), ' · ', item.pageTitle];
    if (item.requiredPermission) {
      facts.push(' · needs ', h('span', { class: 'mono' }, item.requiredPermission));
    }

    return h(
      'li',
      { class: `card menu-item${item.isVisible ? '' : ' is-hidden'}`, 'data-id': item.id },
      h(
        'div',
        { class: 'menu-item-head' },
        createIcon(item.icon),
        h('h3', { class: 'menu-item-label' }, item.label),
        h(
          'span',
          { class: 'menu-item-badges' },
          item.isVisible ? null : h('span', { class: 'badge badge-muted' }, 'Hidden'),
          item.isLocked ? h('span', { class: 'badge badge-trial', title: 'Can be renamed and moved, never hidden or deleted' }, 'Locked') : null,
        ),
      ),
      h('p', { class: 'muted small' }, facts),
      confirming ? this.renderConfirm(item) : this.renderActions(item, ids),
    );
  }

  renderActions(item, ids) {
    return h(
      'div',
      { class: 'menu-item-actions' },
      this.button('Up', {
        label: `Move ${item.label} up`,
        action: 'up',
        item,
        disabled: !canMove(ids, item.id, 'up'),
        onClick: () => this.move(item, ids, 'up'),
      }),
      this.button('Down', {
        label: `Move ${item.label} down`,
        action: 'down',
        item,
        disabled: !canMove(ids, item.id, 'down'),
        onClick: () => this.move(item, ids, 'down'),
      }),
      this.button(item.isVisible ? 'Hide' : 'Show', {
        label: item.isVisible ? `Hide ${item.label} from the menu` : `Show ${item.label} in the menu`,
        action: 'visibility',
        item,
        disabled: item.isLocked,
        onClick: () => this.setVisible(item, !item.isVisible),
      }),
      this.button('Edit', {
        label: `Rename ${item.label} or change its icon`,
        action: 'edit',
        item,
        onClick: () => {
          this.editing = item.id;
          this.draft = null;
          this.confirming = null;
          this.clearMessages();
          this.renderList();
          this.querySelector('#edit-label')?.focus();
        },
      }),
      this.button('Delete', {
        label: `Delete ${item.label} from the menu`,
        action: 'delete',
        item,
        disabled: item.isLocked,
        onClick: () => {
          this.confirming = item.id;
          this.renderList();
          this.findButton(item.id, 'confirm-delete')?.focus();
        },
      }),
    );
  }

  renderConfirm(item) {
    return h(
      'div',
      { class: 'menu-item-actions', role: 'group', 'aria-label': `Delete ${item.label}?` },
      h('p', { class: 'small' }, `Delete “${item.label}” from the menu? The page can be added again.`),
      this.button('Yes, delete', { label: `Yes, delete ${item.label}`, action: 'confirm-delete', item, onClick: () => this.remove(item) }),
      this.button('Cancel', {
        label: `Keep ${item.label}`,
        action: 'cancel-delete',
        item,
        onClick: () => {
          this.confirming = null;
          this.renderList();
          this.findButton(item.id, 'delete')?.focus();
        },
      }),
    );
  }

  renderEdit(item) {
    // What the admin typed survives a save that failed, and the drawing of the list while a save is under way.
    const draft = this.draft?.id === item.id ? this.draft : { label: item.label, icon: item.icon };

    const error = h('p', { class: 'form-error', id: 'edit-error', role: 'alert', hidden: true });
    const label = h('input', { id: 'edit-label', name: 'label', type: 'text', maxlength: String(maxLabelLength), required: true, value: draft.label, 'aria-describedby': 'edit-error' });
    const icon = this.iconSelect('edit-icon', draft.icon);
    const save = h('button', { class: 'button button-primary', type: 'submit', disabled: this.busy }, 'Save');
    const cancel = h('button', { class: 'button', type: 'button', disabled: this.busy }, 'Cancel');

    const leave = () => {
      this.editing = null;
      this.draft = null;
      this.renderList();
      this.findButton(item.id, 'edit')?.focus();
    };
    cancel.addEventListener('click', leave);

    const form = h(
      'form',
      {
        novalidate: true,
        onsubmit: async (event) => {
          event.preventDefault();
          error.hidden = true;
          const problem = validateMenuItem({ label: label.value, icon: icon.value });
          if (problem) {
            error.textContent = problem.message;
            error.hidden = false;
            (problem.field === 'icon' ? icon : label).focus();
            return;
          }
          this.draft = { id: item.id, label: label.value, icon: icon.value };
          await this.run(
            async () => {
              const updated = await this.send(this.itemPath(item), 'PUT', { label: label.value.trim(), icon: icon.value, isVisible: item.isVisible });
              this.replaceItem(updated);
              // Only a saved edit closes the form; a failed one stays open with what was typed.
              this.editing = null;
              this.draft = null;
              return updated;
            },
            { success: (updated) => `Saved “${updated.label}”.`, focus: (page) => page.findButton(item.id, 'edit') },
          );
        },
        onkeydown: (event) => {
          if (event.key === 'Escape') {
            event.stopPropagation();
            leave();
          }
        },
      },
      h('div', { class: 'field' }, h('label', { for: 'edit-label' }, 'Words in the menu'), label),
      h('div', { class: 'field' }, h('label', { for: 'edit-icon' }, 'Icon'), icon),
      error,
      h('div', { class: 'menu-item-actions' }, save, cancel),
    );

    return h('li', { class: 'card menu-item', 'data-id': item.id }, h('h3', {}, `Edit “${item.label}”`), form);
  }

  iconSelect(id, selected) {
    const icons = this.config?.icons ?? [];
    return h(
      'select',
      { id, name: 'icon' },
      icons.map((name) => h('option', { value: name, selected: name === selected }, iconLabel(name))),
    );
  }

  replaceItem(updated) {
    this.config = { ...this.config, items: this.config.items.map((item) => (item.id === updated.id ? updated : item)) };
  }

  // --- The changes --------------------------------------------------------------------------

  move(item, ids, direction) {
    const next = moveItem(ids, item.id, direction);
    return this.run(
      async () => {
        this.config = await this.send(`/v1/site/menus/${this.menuKey}/order`, 'PUT', { itemIds: next });
      },
      {
        success: `Moved “${item.label}” ${direction}.`,
        // Keep the keyboard where the person was; at the end of the list the other arrow is the way on.
        focus: (page) => {
          const here = page.findButton(item.id, direction);
          return here && !here.disabled ? here : page.findButton(item.id, direction === 'up' ? 'down' : 'up');
        },
      },
    );
  }

  setVisible(item, visible) {
    return this.run(
      async () => {
        const updated = await this.send(this.itemPath(item), 'PUT', { label: item.label, icon: item.icon, isVisible: visible });
        this.replaceItem(updated);
      },
      {
        success: visible ? `“${item.label}” is shown in the menu.` : `“${item.label}” is hidden from the menu.`,
        focus: (page) => page.findButton(item.id, 'visibility'),
      },
    );
  }

  remove(item) {
    this.confirming = null;
    return this.run(
      async () => {
        await this.send(this.itemPath(item), 'DELETE');
        await this.fetchConfig();
        this.renderAdd();
      },
      { success: `Removed “${item.label}”. Its page can be added again below.`, focus: (page) => page.listHeading },
    );
  }

  // --- Adding a page ------------------------------------------------------------------------

  renderAdd() {
    const pages = this.config?.availablePages ?? [];
    this.addFieldset = null;

    if (pages.length === 0) {
      replaceChildren(this.addRegion, h('p', { class: 'muted' }, 'Every built-in page is already in this menu.'));
      return;
    }

    const error = h('p', { class: 'form-error', id: 'add-error', role: 'alert', hidden: true });
    const page = h(
      'select',
      { id: 'add-page', name: 'page', 'aria-describedby': 'add-error' },
      pages.map((candidate) => h('option', { value: candidate.key }, `${candidate.title} (${candidate.path})`)),
    );
    const label = h('input', { id: 'add-label', name: 'label', type: 'text', maxlength: String(maxLabelLength), required: true, value: defaultLabelFor(pages[0]), 'aria-describedby': 'add-error' });
    const icon = this.iconSelect('add-icon', 'circle');
    const submit = h('button', { class: 'button button-primary', type: 'submit' }, 'Add to the menu');

    // Choosing another page offers its own title as the words, which the admin can then change.
    page.addEventListener('change', () => {
      label.value = defaultLabelFor(pages.find((candidate) => candidate.key === page.value));
    });

    this.addFieldset = h(
      'fieldset',
      { class: 'plain-fieldset' },
      h('div', { class: 'field' }, h('label', { for: 'add-page' }, 'Page'), page),
      h('div', { class: 'field' }, h('label', { for: 'add-label' }, 'Words in the menu'), label),
      h('div', { class: 'field' }, h('label', { for: 'add-icon' }, 'Icon'), icon),
      error,
      submit,
    );

    const form = h(
      'form',
      {
        class: 'card',
        novalidate: true,
        onsubmit: async (event) => {
          event.preventDefault();
          error.hidden = true;
          const problem = validateMenuItem({ pageKey: page.value, label: label.value, icon: icon.value }, { requirePage: true });
          if (problem) {
            error.textContent = problem.message;
            error.hidden = false;
            ({ pageKey: page, label, icon })[problem.field].focus();
            return;
          }
          await this.run(
            async () => {
              const created = await this.send(`/v1/site/menus/${this.menuKey}/items`, 'POST', {
                pageKey: page.value,
                label: label.value.trim(),
                icon: icon.value,
              });
              await this.fetchConfig();
              this.renderAdd();
              return created;
            },
            { success: (created) => `Added “${created.label}” at the end of the menu.`, focus: (view) => view.listHeading },
          );
        },
      },
      this.addFieldset,
    );

    replaceChildren(this.addRegion, form);
  }
}

customElements.define('admin-menus', AdminMenus);
