import type { SiteMember } from '@/api/types';
import { Badge } from '@/components/ui/Badge';
import { businessModelLabel, formatDate } from '@/lib/format';
import { roleTitle } from '@/lib/roles';

function MemberCard({ member }: { member: SiteMember }) {
  const name = member.fullName.trim();

  return (
    <li className="member">
      <div className="member-who">
        {/* A person with no name on file is shown by their address, once. */}
        <h3 className="member-name">{name.length > 0 ? name : member.email}</h3>
        {name.length > 0 ? <p className="member-email">{member.email}</p> : null}
      </div>
      <div className="member-badges">
        <Badge tone={member.isActive ? 'paid' : 'muted'}>{member.isActive ? 'Active' : 'Inactive'}</Badge>
        <Badge tone="model">{businessModelLabel(member.businessModel)}</Badge>
      </div>
      <dl className="member-facts">
        <div>
          <dt>Organization</dt>
          <dd>{member.agencyName}</dd>
        </div>
        <div>
          <dt>Role</dt>
          <dd>{roleTitle(member.role)}</dd>
        </div>
        <div>
          <dt>Joined</dt>
          <dd>{formatDate(member.joinedAt)}</dd>
        </div>
      </dl>
    </li>
  );
}

/** One card for each membership: the person, whether they can sign in, their organization and role, and when they joined. */
export function MemberList({ members }: { members: readonly SiteMember[] }) {
  return (
    <ul className="member-list">
      {members.map((member) => (
        <MemberCard key={`${member.agencyId}:${member.userId}`} member={member} />
      ))}
    </ul>
  );
}
