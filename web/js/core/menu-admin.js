// What the Site admin's menu editor does that is not drawing: moving an item, checking the form, and wording the
// errors. Plain functions, so they are tested without a browser. The server checks everything again.

import { ApiError } from './api.js';
import { iconNames } from './icons.js';

export const maxLabelLength = 60;

/** The two menus the editor can open, in the order it offers them. */
export const menus = Object.freeze([
  Object.freeze({ key: 'member', title: 'Members', description: 'The sidebar people see inside their organization.' }),
  Object.freeze({ key: 'admin', title: 'Site admin', description: 'The sidebar of this console.' }),
]);

/** Whether an item can move one place in a direction (`up` or `down`) among the ids in order. */
export function canMove(ids, id, direction) {
  const index = ids.indexOf(id);
  if (index < 0) {
    return false;
  }
  return direction === 'up' ? index > 0 : direction === 'down' ? index < ids.length - 1 : false;
}

/** The ids with one item moved a place. The list is returned unchanged when the item cannot move that way. */
export function moveItem(ids, id, direction) {
  if (!canMove(ids, id, direction)) {
    return [...ids];
  }
  const index = ids.indexOf(id);
  const target = direction === 'up' ? index - 1 : index + 1;
  const next = [...ids];
  [next[index], next[target]] = [next[target], next[index]];
  return next;
}

/**
 * Checks the add or edit form before anything is sent.
 * @param {{ label?: string, icon?: string, pageKey?: string }} fields
 * @param {{ requirePage?: boolean }} [options] An item being added needs a page; one being edited keeps its own.
 * @returns {{ field: 'pageKey' | 'label' | 'icon', message: string } | null} The first problem found, or null.
 */
export function validateMenuItem({ label, icon, pageKey }, { requirePage = false } = {}) {
  if (requirePage && (typeof pageKey !== 'string' || pageKey.length === 0)) {
    return { field: 'pageKey', message: 'Choose a page to add.' };
  }
  if (typeof label !== 'string' || label.trim().length === 0) {
    return { field: 'label', message: 'Enter the words the menu should show.' };
  }
  if (label.trim().length > maxLabelLength) {
    return { field: 'label', message: `The words must be at most ${maxLabelLength} characters.` };
  }
  // eslint-disable-next-line no-control-regex
  if (/[\u0000-\u001f\u007f-\u009f]/.test(label)) {
    return { field: 'label', message: 'The words cannot contain line breaks or other control characters.' };
  }
  if (typeof icon !== 'string' || !iconNames.includes(icon)) {
    return { field: 'icon', message: 'Choose an icon from the list.' };
  }
  return null;
}

/** The label to start an add form with when a page is chosen: the page's own title. */
export function defaultLabelFor(page) {
  return typeof page?.title === 'string' ? page.title.slice(0, maxLabelLength) : '';
}

/** What to tell the Site admin when an edit to a menu failed. */
export function messageForMenuError(error) {
  if (error instanceof ApiError) {
    switch (error.code) {
      case 'menu_item_locked':
        return 'This item is locked: it can be renamed and moved, but not hidden or deleted.';
      case 'page_already_in_menu':
        return 'That page is already in the menu.';
      case 'page_not_found':
        return 'That page cannot go in this menu.';
      case 'menu_item_not_found':
        return 'That item no longer exists. The list has been reloaded.';
      case 'menu_changed':
        return 'The menu changed since you opened it. The list has been reloaded; try again.';
      case 'menu_not_found':
        return 'There is no such menu.';
      default:
        break;
    }
    if (error.status === 403) {
      return 'This account is no longer a site admin.';
    }
    if (error.status === 429) {
      return 'Too many changes at once. Wait a moment, then try again.';
    }
    if (error.status === 400 && error.errors) {
      const first = Object.values(error.errors).flat()[0];
      if (typeof first === 'string') {
        return first;
      }
    }
    return 'The change could not be saved. Try again in a moment.';
  }
  return 'Could not reach the server. Check your connection and try again.';
}

/** What to show when a menu could not be loaded. */
export function messageForMenuLoadError(error) {
  if (error instanceof ApiError) {
    if (error.status === 403) {
      return 'This account is no longer a site admin.';
    }
    if (error.status === 429) {
      return 'Too many requests. Wait a minute, then try again.';
    }
    return 'The menu could not be loaded.';
  }
  return 'Could not reach the server. Check your connection and try again.';
}

/** After an error that means the list on screen is out of date, the list is read again. */
export function shouldReloadAfter(error) {
  return error instanceof ApiError && (error.code === 'menu_item_not_found' || error.code === 'menu_changed');
}
