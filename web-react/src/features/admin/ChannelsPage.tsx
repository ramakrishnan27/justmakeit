import { useRef, useState } from 'react';
import type { SiteChannel } from '@/api/types';
import { ChannelIcon } from '@/components/icons/ChannelIcon';
import { UiIcon } from '@/components/icons/UiIcon';
import { FeedbackLine, type Feedback } from '@/components/ui/Feedback';
import { ErrorNotice, Loading } from '@/components/ui/Notice';
import { PageTitle } from '@/components/ui/PageTitle';
import { countLabel, formatDate } from '@/lib/format';
import { messageForSiteLoadError } from '@/lib/messages';
import { NewChannelDialog } from './NewChannelDialog';
import { useSiteChannels } from './useSiteChannels';

function ChannelTile({ channel }: { channel: SiteChannel }) {
  return (
    <li className="channel">
      <span className="channel-mark">
        <ChannelIcon channel={channel} />
      </span>
      <span className="channel-name">{channel.name}</span>
      {channel.description.length > 0 ? <span className="channel-description">{channel.description}</span> : null}
      <span className="channel-used">{`Used by ${countLabel(channel.organizationCount, 'organization')}`}</span>
      <span className="channel-meta">
        {channel.createdByName === null ? 'Built in' : `Added by ${channel.createdByName} · ${formatDate(channel.createdAt)}`}
      </span>
    </li>
  );
}

/**
 * The Site admin's Channels page: every channel the platform sends through, as a list of icons with the name, what it is
 * for and how many organizations have it, and a button to add a channel of your own. Which organization has which channel is
 * set from Users, Organizations. Read through /v1/site, which the server answers only to a Site admin (and checks the
 * database each time).
 */
export default function ChannelsPage() {
  const { state, reload, create } = useSiteChannels();
  const [adding, setAdding] = useState(false);
  const [feedback, setFeedback] = useState<Feedback | null>(null);
  const newButton = useRef<HTMLButtonElement>(null);

  return (
    <>
      <PageTitle documentTitle="Channels">Channels</PageTitle>
      <p className="muted">Where the platform sends messages and posts. Add a channel here, then turn it on for organizations from Users, Organizations.</p>

      <div className="page-actions">
        <button
          ref={newButton}
          className="button button-primary button-pill"
          type="button"
          onClick={() => {
            setFeedback(null);
            setAdding(true);
          }}
        >
          <UiIcon name="plus" />
          New channel
        </button>
      </div>

      <FeedbackLine feedback={feedback} />

      <div aria-live="polite">
        {state.status === 'loading' ? <Loading /> : null}
        {state.status === 'error' ? (
          <ErrorNotice message={messageForSiteLoadError(state.error, 'The channels')}>
            <button className="button" type="button" onClick={reload}>
              Try again
            </button>
          </ErrorNotice>
        ) : null}
        {state.status === 'ready' && state.channels.length === 0 ? (
          <div className="notice">
            <p>No channels yet. Add the first one with New channel.</p>
          </div>
        ) : null}
        {state.status === 'ready' && state.channels.length > 0 ? (
          <ul className="channel-grid" aria-label="Channels">
            {state.channels.map((channel) => (
              <ChannelTile key={channel.id} channel={channel} />
            ))}
          </ul>
        ) : null}
      </div>

      {adding ? (
        <NewChannelDialog
          create={create}
          returnFocus={newButton}
          onClose={() => setAdding(false)}
          onCreated={(channel) => {
            setAdding(false);
            setFeedback({ tone: 'success', text: `${channel.name} was added. Turn it on for organizations from Users, Organizations.` });
          }}
        />
      ) : null}
    </>
  );
}
