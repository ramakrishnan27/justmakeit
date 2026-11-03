import type { OrganizationRole } from '@/api/types';
import { Badge } from '@/components/ui/Badge';
import { Section } from '@/components/ui/Section';
import { upcomingAreas } from '@/lib/roles';

/** The areas still to come for the person's role. None of them exist yet, and the page says so. */
export function UpcomingSection({ role }: { role: OrganizationRole }) {
  return (
    <Section title="Coming soon for your role">
      <ul className="soon-list">
        {upcomingAreas(role).map((area) => (
          <li className="card soon-item" key={area.title}>
            <h3>
              {area.title} <Badge tone="trial">Coming soon</Badge>
            </h3>
            <p className="muted small">{area.text}</p>
          </li>
        ))}
      </ul>
    </Section>
  );
}
