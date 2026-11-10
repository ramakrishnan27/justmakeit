import { Can } from '@/access/Can';
import { PERMISSIONS } from '@/access/permissions';
import { ErrorNotice, Loading } from '@/components/ui/Notice';
import { PageTitle } from '@/components/ui/PageTitle';
import { messageForWorkspacesLoadError } from '@/lib/messages';
import { allowsSeveralWorkspaces, workspaceWords } from '@/lib/roles';
import { useMember } from '@/session/SessionContext';
import { AddWorkspaceForm } from './AddWorkspaceForm';
import { useWorkspaces } from './useWorkspaces';
import { WorkspaceList } from './WorkspaceList';

/**
 * The workspaces of the person's organization: a client workspace for each client in an agency, the one workspace of a
 * creator, the workspaces of a company. The add form is shown only to someone whose access token carries the
 * workspaces.create permission, in an organization that may have several; the server checks both again.
 */
export default function WorkspacesPage() {
  const { organization } = useMember();
  const words = workspaceWords(organization.businessModel);
  const { state, reload } = useWorkspaces();

  return (
    <>
      <PageTitle documentTitle={words.many === 'Your workspace' ? 'Workspaces' : words.many}>{words.many}</PageTitle>
      <p className="muted">{`The ${words.one}s of ${organization.name || 'your organization'}.`}</p>

      <div aria-live="polite">
        {state.status === 'loading' ? <Loading /> : null}

        {state.status === 'error' ? (
          <ErrorNotice message={messageForWorkspacesLoadError(state.error, words.one)}>
            <button className="button" type="button" onClick={reload}>
              Try again
            </button>
          </ErrorNotice>
        ) : null}

        {state.status === 'ready' ? (
          <div>
            <WorkspaceList workspaces={state.workspaces} word={words.one} />
            {/* An individual creator has the one workspace they started with. */}
            {allowsSeveralWorkspaces(organization.businessModel) ? (
              <Can permission={PERMISSIONS.workspacesCreate}>
                <AddWorkspaceForm word={words.one} onAdded={reload} />
              </Can>
            ) : null}
          </div>
        ) : null}
      </div>
    </>
  );
}
