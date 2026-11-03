import type { ReactNode } from 'react';
import { useCan } from './hooks';
import type { PermissionRule } from './permissions';

interface CanProps extends PermissionRule {
  /** Shown instead of the children when the person lacks the permission. Nothing by default. */
  fallback?: ReactNode;
  children: ReactNode;
}

/**
 * Renders its children only for a person whose access token holds the permission, and the fallback otherwise.
 * The children are not rendered at all, so a component that is not allowed does not mount, fetch or run its effects.
 *
 *     <Can permission={PERMISSIONS.workspacesCreate}><AddWorkspaceForm /></Can>
 *     <Can anyOf={[PERMISSIONS.dataWrite, PERMISSIONS.organizationManage]} fallback={<ReadOnlyNote />}>...</Can>
 *
 * This chooses what to show; it is not access control. The server checks the same permission on every request.
 */
export function Can({ fallback = null, children, ...rule }: CanProps) {
  return <>{useCan(rule) ? children : fallback}</>;
}
