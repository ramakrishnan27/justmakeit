import { Link, useSearchParams } from 'react-router-dom';
import type { SiteMember, SiteOrganization } from '@/api/types';
import { PageTitle } from '@/components/ui/PageTitle';
import { Section } from '@/components/ui/Section';
import { MemberList } from './MemberList';
import { OrganizationManageList } from './OrganizationManageList';
import { SitePagedList } from './SitePagedList';
import { useSitePagedList } from './useSitePagedList';

type Tab = 'members' | 'organizations';

const tabs: ReadonlyArray<{ key: Tab; label: string }> = [
  { key: 'members', label: 'Members' },
  { key: 'organizations', label: 'Organizations' },
];

function MembersPanel() {
  const { state, reload, goTo } = useSitePagedList<SiteMember>('/v1/site/members');

  return (
    <Section title="Members">
      <div aria-live="polite">
        <SitePagedList
          state={state}
          subject="The members"
          pagerLabel="Member pages"
          empty="No members yet. People appear here as they register or accept an invitation."
          onReload={reload}
          onPage={goTo}
        >
          {(items) => <MemberList members={items} />}
        </SitePagedList>
      </div>
    </Section>
  );
}

function OrganizationsPanel() {
  const { state, reload, goTo } = useSitePagedList<SiteOrganization>('/v1/site/agencies');

  return (
    <Section title="Organizations">
      <div aria-live="polite">
        <SitePagedList
          state={state}
          subject="The organizations"
          pagerLabel="Organization pages"
          empty="No organizations yet. They appear here as people register."
          onReload={reload}
          onPage={goTo}
        >
          {(items) => <OrganizationManageList organizations={items} />}
        </SitePagedList>
      </div>
    </Section>
  );
}

/**
 * The Site admin's Users page: everyone on the platform, as two lists, Members (each person with their organization and
 * role) and Organizations, where each organization opens to its channels and can be deactivated. The list shown is in the
 * address (`?tab=organizations`), so a reload or the back button keeps it, and only the list that is shown is read from the server. Read through /v1/site, which the server answers only to a
 * Site admin (and checks the database each time).
 */
export default function UsersPage() {
  const [search] = useSearchParams();
  const active: Tab = search.get('tab') === 'organizations' ? 'organizations' : 'members';

  return (
    <>
      <PageTitle documentTitle="Users">Users</PageTitle>
      <p className="muted">Everyone on the platform: the people, and the organizations they belong to.</p>

      <nav className="tabs" aria-label="Users lists">
        {tabs.map(({ key, label }) => (
          <Link
            key={key}
            className="tab"
            to={{ search: key === 'members' ? '' : `?tab=${key}` }}
            replace
            aria-current={key === active ? 'page' : undefined}
          >
            {label}
          </Link>
        ))}
      </nav>

      {active === 'members' ? <MembersPanel /> : <OrganizationsPanel />}
    </>
  );
}
