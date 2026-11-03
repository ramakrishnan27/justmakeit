import type { Organization } from '@/api/types';
import { Badge } from '@/components/ui/Badge';
import { Section } from '@/components/ui/Section';
import { formatDate, planLabel } from '@/lib/format';
import { useSessionActions } from '@/session/SessionContext';

/** What the access token says about this session, and the permissions it carries. For display only. */
export function SessionSection({ organization, permissions }: { organization: Organization; permissions: readonly string[] }) {
  const claims = useSessionActions().getClaims() ?? {};
  const expires = typeof claims.exp === 'number' ? formatDate(claims.exp * 1000, { withTime: true }) : '–';

  return (
    <Section title="Your session">
      <div className="card">
        <dl className="kv">
          <div>
            <dt>User ID</dt>
            <dd className="mono">{String(claims.user_id ?? '–')}</dd>
          </div>
          <div>
            <dt>Role</dt>
            <dd>
              <Badge tone="accent">{String(claims.role ?? '–')}</Badge>
            </dd>
          </div>
          <div>
            <dt>Plan</dt>
            <dd>{planLabel(organization.plan)}</dd>
          </div>
          <div>
            <dt>Token expires</dt>
            <dd>{expires}</dd>
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
        <p className="muted small">These come from your access token. It is kept in memory only and is renewed automatically while you work.</p>
      </div>
    </Section>
  );
}
