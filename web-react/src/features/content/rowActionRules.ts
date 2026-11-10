// Which actions a listed post offers, for a person with these permissions. The server decides again on every request; this only
// chooses what to show, so nobody is offered a button that can only fail.

import type { ContentStatus } from '@/api/content';

export type RowAction = 'edit' | 'open' | 'duplicate' | 'unschedule' | 'archive' | 'restore' | 'delete';

export interface Abilities {
  /** Holds contentstudio.create. */
  canCreate: boolean;
  /** Holds contentstudio.publish. */
  canPublish: boolean;
}

/** The statuses in which the content of a post can still be changed. Same as ContentStatusMachine.IsEditable. */
export function isEditable(status: ContentStatus): boolean {
  return status === 'draft' || status === 'scheduled' || status === 'failed';
}

/** The actions for a post in this status, in the order the menu shows them. Always starts with Edit or Open. */
export function availableActions(status: ContentStatus, { canCreate, canPublish }: Abilities): RowAction[] {
  const actions: RowAction[] = [canCreate && isEditable(status) ? 'edit' : 'open'];

  if (canCreate) {
    actions.push('duplicate');
  }
  if (canPublish && status === 'scheduled') {
    actions.push('unschedule');
  }
  if (canPublish && (status === 'draft' || status === 'failed' || status === 'published')) {
    actions.push('archive');
  }
  if (canPublish && status === 'archived') {
    actions.push('restore');
  }
  // A draft is the writer's to delete; anything that went through publishing is the publisher's. A scheduled or published post is never deleted.
  if ((status === 'draft' && canCreate) || ((status === 'failed' || status === 'archived') && canPublish)) {
    actions.push('delete');
  }
  return actions;
}

export const actionLabels: Record<RowAction, string> = {
  edit: 'Edit',
  open: 'Open',
  duplicate: 'Duplicate',
  unschedule: 'Unschedule',
  archive: 'Archive',
  restore: 'Restore',
  delete: 'Delete',
};
