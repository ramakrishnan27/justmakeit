import type { Workspace } from '@/api/types';
import { countLabel, formatDate } from '@/lib/format';

export function WorkspaceList({ workspaces, word }: { workspaces: readonly Workspace[]; word: string }) {
  return (
    <>
      <p className="muted small">{countLabel(workspaces.length, word)}</p>
      <ul className="workspace-list">
        {workspaces.map((workspace) => (
          <li className="card workspace" key={workspace.id}>
            <h3 className="workspace-name">{workspace.name}</h3>
            <p className="muted small">Created {formatDate(workspace.createdAt)}</p>
          </li>
        ))}
      </ul>
    </>
  );
}
