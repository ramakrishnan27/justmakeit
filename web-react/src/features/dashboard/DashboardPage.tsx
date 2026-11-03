import { PageTitle } from '@/components/ui/PageTitle';
import { roleTitle, workspaceWords } from '@/lib/roles';
import { useSignedIn } from '@/session/SessionContext';
import { AbilitiesSection } from './AbilitiesSection';
import { RoleSection } from './RoleSection';
import { SessionSection } from './SessionSection';
import { UpcomingSection } from './UpcomingSection';

/**
 * The member dashboard. One page that changes with the person's role in their kind of organization: it says what the
 * role is for, what it may do, and lists what is still to come for that role. The frame around it (header, sidebar) is
 * the app shell; the workspaces have a page of their own.
 */
export default function DashboardPage() {
  const { user, role, organization, permissions } = useSignedIn();
  const firstName = user.fullName.trim().split(/\s+/)[0] || user.email || 'there';

  return (
    <>
      <PageTitle documentTitle="Dashboard">{`Welcome, ${firstName}`}</PageTitle>
      <p className="muted">{`${roleTitle(role)} at ${organization.name || 'your organization'}`}</p>

      <RoleSection role={role} model={organization.businessModel} />
      <AbilitiesSection permissions={permissions} workspaceWord={workspaceWords(organization.businessModel).one} />
      <UpcomingSection role={role} />
      <SessionSection organization={organization} permissions={permissions} />
    </>
  );
}
