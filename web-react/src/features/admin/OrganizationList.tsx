import type { SiteOrganization } from '@/api/types';
import { Badge } from '@/components/ui/Badge';
import { businessModelLabel, formatDate, formatNumber, planLabel } from '@/lib/format';

function OrganizationCard({ organization }: { organization: SiteOrganization }) {
  return (
    <li className="agency">
      <h3 className="agency-name">{organization.name}</h3>
      <div className="agency-badges">
        <Badge tone="model">{businessModelLabel(organization.businessModel)}</Badge>
        <Badge tone={organization.plan === 'paid' ? 'paid' : 'trial'}>{planLabel(organization.plan)}</Badge>
        {/* The dashboard only shows it; deactivating is done from Users, Organizations. */}
        {organization.isActive === false ? <Badge tone="danger">Inactive</Badge> : null}
      </div>
      <dl className="agency-facts">
        <div>
          <dt>Members</dt>
          <dd>{formatNumber(organization.members)}</dd>
        </div>
        <div>
          <dt>Workspaces</dt>
          <dd>{formatNumber(organization.workspaces)}</dd>
        </div>
        <div>
          <dt>Created</dt>
          <dd>{formatDate(organization.createdAt)}</dd>
        </div>
      </dl>
    </li>
  );
}

/** One card for each organization: its name, kind, plan, members, workspaces and when it was created. */
export function OrganizationList({ organizations }: { organizations: readonly SiteOrganization[] }) {
  return (
    <ul className="agency-list">
      {organizations.map((organization) => (
        <OrganizationCard key={organization.id} organization={organization} />
      ))}
    </ul>
  );
}
