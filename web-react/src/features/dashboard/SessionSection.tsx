import type { Organization } from '@/api/types';
import { Badge } from '@/components/ui/Badge';
import { Section } from '@/components/ui/Section';
import { formatDate, planLabel } from '@/lib/format';
import { useMember } from '@/session/SessionContext';

/** Who is signed in, until when, and the permissions the sign-in carries. For display only; the server decides what a sign-in may do. */
export function SessionSection({ organization, permissions }: { organization: Organization; permissions: readonly string[] }) {
  const { user, role, expiresAt } = useMember();

  return (
    <Section title="Your session">
      <div className="card">
        <dl className="kv">
          <div>
            <dt>User ID</dt>
            <dd className="mono">{user.id}</dd>
          </div>
          <div>
            <dt>Role</dt>
            <dd>
              <Badge tone="accent">{role}</Badge>
            </dd>
          </div>
          <div>
            <dt>Plan</dt>
            <dd>{planLabel(organization.plan)}</dd>
          </div>
          <div>
            <dt>Signed in until</dt>
            <dd>{formatDate(expiresAt, { withTime: true })}</dd>
          </div>
        </dl>
        <div className="permission-chips">
          <p className="small muted">Your permissions</p>
          <ul className="chip-list">
            {permissions.map((permission) => (
              <li key={permission}>
                <Badge mono>{permission}</Badge>
              </li>
            ))}
          </ul>
        </div>
        <p className="muted small">
          Your sign-in is kept in a cookie this page cannot read. It is not renewed: when it ends, or when your permissions change, you sign in again.
        </p>
      </div>
    </Section>
  );
}
