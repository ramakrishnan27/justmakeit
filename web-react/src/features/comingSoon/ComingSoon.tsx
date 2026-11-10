import type { ReactNode } from 'react';
import { Icon } from '@/components/icons/Icon';
import { Badge } from '@/components/ui/Badge';
import { PageTitle } from '@/components/ui/PageTitle';

/**
 * A page for a feature that is not built yet. The menu item, the permission and the address are real (the platform
 * administrator can already rename, move, hide or take the item away per role), the page says plainly that nothing
 * works here yet, so nobody is led to expect a feature that does not exist.
 */
export function ComingSoon({ title, icon, children }: { title: string; icon: string; children: ReactNode }) {
  return (
    <>
      <PageTitle documentTitle={title}>{title}</PageTitle>
      <section className="card coming-soon" aria-label={`${title}: coming soon`}>
        <span className="coming-soon-mark">
          <Icon name={icon} />
        </span>
        <div className="coming-soon-text">
          <p>
            <Badge tone="trial">Coming soon</Badge>
          </p>
          <p>{children}</p>
          <p className="muted small">This part of the platform is not built yet, so there is nothing to use here for now.</p>
        </div>
      </section>
    </>
  );
}
