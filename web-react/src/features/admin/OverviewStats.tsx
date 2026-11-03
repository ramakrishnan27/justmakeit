import type { SiteOverview } from '@/api/types';
import { formatNumber } from '@/lib/format';

function StatTile({ label, value, note }: { label: string; value: number; note?: string }) {
  return (
    <div className="stat">
      <p className="stat-label">{label}</p>
      <p className="stat-value">{formatNumber(value)}</p>
      {note ? <p className="stat-note">{note}</p> : null}
    </div>
  );
}

/** The platform totals: organizations (with how many are on trial and paid), users, workspaces and memberships. */
export function OverviewStats({ overview }: { overview: SiteOverview }) {
  return (
    <div className="stats">
      <StatTile
        label="Organizations"
        value={overview.agencies}
        note={`${formatNumber(overview.trialAgencies)} trial · ${formatNumber(overview.paidAgencies)} paid`}
      />
      <StatTile label="Users" value={overview.users} />
      <StatTile label="Workspaces" value={overview.workspaces} />
      <StatTile label="Memberships" value={overview.memberships} />
    </div>
  );
}
