import { useSession } from '@/session/SessionContext';
import { satisfies, type PermissionRule } from './permissions';

const none: readonly string[] = [];

/** The permissions in the person's access token. Empty when nobody is signed in. */
export function usePermissions(): readonly string[] {
  const state = useSession();
  return state.status === 'signedIn' ? state.permissions : none;
}

/** Whether the person holds what the rule asks for. The component renders again if their permissions change. */
export function useCan(rule: PermissionRule): boolean {
  return satisfies(usePermissions(), rule);
}
