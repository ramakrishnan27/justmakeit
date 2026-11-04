import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { ApiError } from '@/api/client';
import type { CreatedInvitation, OrganizationRole, Team } from '@/api/types';
import { useApi } from '@/session/SessionContext';
import type { TeamRow } from './teamRows';

export type TeamState = { status: 'loading' } | { status: 'error'; error: unknown } | { status: 'ready'; team: Team };

export interface RowFailure {
  row: TeamRow;
  error: unknown;
}

/** What the page can do to the team. Each one reads the team again when it is done, so the list shows what the server holds. */
export interface TeamActions {
  /** Makes an invitation. The answer carries the secret of its link, and nothing else ever does. */
  invite(email: string, role: OrganizationRole): Promise<CreatedInvitation>;
  /** Gives a person another role. It ends their current sign-in: they sign in again with the new one. */
  changeRole(userId: string, role: OrganizationRole): Promise<void>;
  /** Removes people and cancels invitations, one after the other. Resolves with the ones the server refused. */
  removeRows(rows: readonly TeamRow[]): Promise<RowFailure[]>;
}

/**
 * Loads the team (people and waiting invitations). `reload` shows the loading line again (to retry); the actions read
 * quietly, so the list stays on screen and nothing the person is doing is thrown away.
 */
export function useTeam() {
  const api = useApi();
  const [state, setState] = useState<TeamState>({ status: 'loading' });
  const [attempt, setAttempt] = useState(0);
  const mounted = useRef(true);

  useEffect(() => {
    mounted.current = true;
    return () => {
      mounted.current = false;
    };
  }, []);

  useEffect(() => {
    let active = true;
    api.request<Team>('/v1/members').then(
      (team) => {
        if (active) {
          setState({ status: 'ready', team });
        }
      },
      (error: unknown) => {
        if (active) {
          setState({ status: 'error', error });
        }
      },
    );
    return () => {
      active = false;
    };
  }, [api, attempt]);

  const reload = useCallback(() => {
    setState({ status: 'loading' });
    setAttempt((count) => count + 1);
  }, []);

  // A failed quiet read keeps what is on screen; a session that really ended is announced by the request itself.
  const refresh = useCallback(async () => {
    try {
      const team = await api.request<Team>('/v1/members');
      if (mounted.current) {
        setState({ status: 'ready', team });
      }
    } catch {
      // The list on screen stays.
    }
  }, [api]);

  const actions = useMemo<TeamActions>(
    () => ({
      async invite(email, role) {
        try {
          return await api.request<CreatedInvitation>('/v1/members/invitations', { method: 'POST', body: { email, role } });
        } finally {
          await refresh();
        }
      },

      async changeRole(userId, role) {
        try {
          await api.request(`/v1/members/${encodeURIComponent(userId)}/role`, { method: 'PUT', body: { role } });
        } finally {
          await refresh();
        }
      },

      async removeRows(rows) {
        const failures: RowFailure[] = [];
        for (const row of rows) {
          const path =
            row.kind === 'member' ? `/v1/members/${encodeURIComponent(row.id)}` : `/v1/members/invitations/${encodeURIComponent(row.id)}`;
          try {
            await api.request(path, { method: 'DELETE' });
          } catch (error) {
            failures.push({ row, error });
            // The session is over: the rest would only be refused the same way.
            if (error instanceof ApiError && error.status === 401) {
              break;
            }
          }
        }
        await refresh();
        return failures;
      },
    }),
    [api, refresh],
  );

  return { state, reload, actions };
}
