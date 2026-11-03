import type { BusinessModel, OrganizationRole } from '@/api/types';
import { Badge } from '@/components/ui/Badge';
import { Section } from '@/components/ui/Section';
import { responsibilities, roleTitle } from '@/lib/roles';

/** The person's role, and what it is for in their kind of organization. */
export function RoleSection({ role, model }: { role: OrganizationRole; model: BusinessModel }) {
  return (
    <Section title="Your role">
      <div className="card role-card" data-role={role}>
        <p>
          <Badge tone="accent">{roleTitle(role)}</Badge>
        </p>
        <p>{responsibilities(model, role) ?? 'Your role gives you access to this organization.'}</p>
      </div>
    </Section>
  );
}
