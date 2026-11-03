import { Badge } from '@/components/ui/Badge';
import { Section } from '@/components/ui/Section';
import { formatDate } from '@/lib/format';
import { useSiteAdmin } from '@/session/SessionContext';

/** Who is signed in and until when. For display only; the server decides what a sign-in may do. */
export function AdminSessionSection() {
  const { user, role, expiresAt } = useSiteAdmin();

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
            <dt>Signed in until</dt>
            <dd>{formatDate(expiresAt, { withTime: true })}</dd>
          </div>
        </dl>
        <p className="muted small">
          Your sign-in is kept in a cookie this page cannot read. It is not renewed: when it ends you sign in again.
        </p>
      </div>
    </Section>
  );
}
