import type { OrganizationChannel } from '@/api/types';
import { ChannelIcon } from '@/components/icons/ChannelIcon';
import { Badge } from '@/components/ui/Badge';
import { ErrorNotice, Loading } from '@/components/ui/Notice';
import { PageTitle } from '@/components/ui/PageTitle';
import { platformIconUrl } from '@/lib/channelForm';
import { messageForPlatformsLoadError } from '@/lib/messages';
import { usePlatforms } from './usePlatforms';

function PlatformTile({ platform }: { platform: OrganizationChannel }) {
  return (
    <li className="channel">
      <span className="channel-mark">
        <ChannelIcon channel={platform} iconSrc={platformIconUrl(platform.id)} />
      </span>
      <span className="channel-name">{platform.name}</span>
      {platform.description.length > 0 ? <span className="channel-description">{platform.description}</span> : null}
      {/* Words, not colour alone. */}
      <Badge tone={platform.isEnabled ? 'paid' : 'muted'}>{platform.isEnabled ? 'Enabled' : 'Not enabled'}</Badge>
    </li>
  );
}

/**
 * The Platform page: every platform (channel) the service supports, as a list of icons with the name, what it is for and whether
 * the platform administrator has enabled it for this organization. Read only. Opened only by someone whose access token carries
 * platforms.view; the server checks the permission again, in the endpoint and in the database.
 */
export default function PlatformsPage() {
  const { state, reload } = usePlatforms();
  const enabled = state.status === 'ready' ? state.platforms.filter((platform) => platform.isEnabled).length : 0;

  return (
    <>
      <PageTitle documentTitle="Platforms">Platforms</PageTitle>
      <p className="muted">The channels you can reach people through. The platform administrator turns them on for your organization.</p>

      <div aria-live="polite">
        {state.status === 'loading' ? <Loading /> : null}

        {state.status === 'error' ? (
          <ErrorNotice message={messageForPlatformsLoadError(state.error)}>
            <button className="button" type="button" onClick={reload}>
              Try again
            </button>
          </ErrorNotice>
        ) : null}

        {state.status === 'ready' && state.platforms.length === 0 ? (
          <div className="notice">
            <p>There are no platforms yet.</p>
          </div>
        ) : null}

        {state.status === 'ready' && state.platforms.length > 0 ? (
          <>
            <p className="muted small">{`${enabled} of ${state.platforms.length} enabled`}</p>
            <ul className="channel-grid" aria-label="Platforms">
              {state.platforms.map((platform) => (
                <PlatformTile key={platform.id} platform={platform} />
              ))}
            </ul>
          </>
        ) : null}
      </div>
    </>
  );
}
