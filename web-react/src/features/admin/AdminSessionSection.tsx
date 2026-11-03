import { Badge } from '@/components/ui/Badge';
import { Section } from '@/components/ui/Section';
import { formatDate } from '@/lib/format';
import { useSessionActions } from '@/session/SessionContext';

/** What the access token says about this session. For display only; the server decides what the token may do. */
export function AdminSessionSection() {
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
            <dt>Token expires</dt>
            <dd>{expires}</dd>
          </div>
        </dl>
        <p className="muted small">These come from your access token. It is kept in memory only and is renewed automatically while you work.</p>
      </div>
    </Section>
  );
}
