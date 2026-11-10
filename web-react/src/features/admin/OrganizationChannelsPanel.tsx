import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import type { OrganizationChannel } from '@/api/types';
import { ChannelIcon } from '@/components/icons/ChannelIcon';
import { ErrorNotice, Loading } from '@/components/ui/Notice';
import { messageForOrganizationChannelError, messageForOrganizationChannelsLoadError } from '@/lib/organizationAdmin';
import { useApi } from '@/session/SessionContext';

type LoadState = { status: 'loading' } | { status: 'error'; error: unknown } | { status: 'ready'; channels: OrganizationChannel[] };

/**
 * The channels of one organization, each with a switch: on means the Site admin has turned the channel on for it. The list
 * is read when the panel first appears (the card keeps it mounted afterwards, so closing and opening it again reads nothing).
 * A switch is saved at once: it moves first and is put back, with the reason, if the server says no. `onEnabledCount` tells
 * the card how many are on, so its own figure follows the switches.
 */
export function OrganizationChannelsPanel({
  organizationId,
  organizationName,
  onEnabledCount,
}: {
  organizationId: string;
  organizationName: string;
  onEnabledCount: (count: number) => void;
}) {
  const api = useApi();
  const [state, setState] = useState<LoadState>({ status: 'loading' });
  const [attempt, setAttempt] = useState(0);
  const [busy, setBusy] = useState<ReadonlySet<string>>(new Set());
  const [problem, setProblem] = useState<string | null>(null);

  useEffect(() => {
    let active = true;
    api.request<OrganizationChannel[]>(`/v1/site/agencies/${encodeURIComponent(organizationId)}/channels`).then(
      (channels) => {
        if (active) {
          setState({ status: 'ready', channels });
        }
      },
      (error: unknown) => {
        if (active) {
          setState({ status: 'error', error });
        }
      },
    );
    return () => {
      active = false;
    };
  }, [api, organizationId, attempt]);

  const enabledCount = state.status === 'ready' ? state.channels.filter((channel) => channel.isEnabled).length : null;
  useEffect(() => {
    if (enabledCount !== null) {
      onEnabledCount(enabledCount);
    }
  }, [enabledCount, onEnabledCount]);

  function setEnabledLocally(channelId: string, isEnabled: boolean) {
    setState((current) =>
      current.status === 'ready'
        ? { status: 'ready', channels: current.channels.map((channel) => (channel.id === channelId ? { ...channel, isEnabled } : channel)) }
        : current,
    );
  }

  function setBusyFor(channelId: string, on: boolean) {
    setBusy((current) => {
      const next = new Set(current);
      if (on) {
        next.add(channelId);
      } else {
        next.delete(channelId);
      }
      return next;
    });
  }

  async function toggle(channel: OrganizationChannel) {
    const enabled = !channel.isEnabled;
    setProblem(null);
    setBusyFor(channel.id, true);
    setEnabledLocally(channel.id, enabled);
    try {
      await api.request(`/v1/site/agencies/${encodeURIComponent(organizationId)}/channels/${encodeURIComponent(channel.id)}`, {
        method: 'PUT',
        body: { enabled },
      });
    } catch (error) {
      setEnabledLocally(channel.id, channel.isEnabled);
      setProblem(messageForOrganizationChannelError(error, channel.name));
    } finally {
      setBusyFor(channel.id, false);
    }
  }

  if (state.status === 'loading') {
    return <Loading text="Loading channels…" />;
  }

  if (state.status === 'error') {
    return (
      <ErrorNotice message={messageForOrganizationChannelsLoadError(state.error)}>
        <button
          className="button"
          type="button"
          onClick={() => {
            setState({ status: 'loading' });
            setAttempt((count) => count + 1);
          }}
        >
          Try again
        </button>
      </ErrorNotice>
    );
  }

  if (state.channels.length === 0) {
    return (
      <p className="muted">
        There are no channels yet. Add one on the <Link to="/admin/channels">Channels page</Link>.
      </p>
    );
  }

  return (
    <div>
      <p className="muted small">{`Turn a channel on or off for ${organizationName}. Each change is saved at once.`}</p>
      {problem ? (
        <p className="feedback feedback-error" role="alert">
          {problem}
        </p>
      ) : null}
      <ul className="org-channels">
        {state.channels.map((channel) => {
          const inputId = `org-${organizationId}-channel-${channel.id}`;
          return (
            <li key={channel.id} className="org-channel">
              <span className="channel-mark channel-mark-small">
                <ChannelIcon channel={channel} />
              </span>
              <span className="org-channel-text">
                <label className="org-channel-name" htmlFor={inputId}>
                  {channel.name}
                </label>
                {channel.description.length > 0 ? (
                  <span className="org-channel-description" id={`${inputId}-description`}>
                    {channel.description}
                  </span>
                ) : null}
              </span>
              <span className="switch">
                <input
                  id={inputId}
                  type="checkbox"
                  role="switch"
                  checked={channel.isEnabled}
                  disabled={busy.has(channel.id)}
                  aria-describedby={channel.description.length > 0 ? `${inputId}-description` : undefined}
                  onChange={() => void toggle(channel)}
                />
                <span className="switch-state" aria-hidden="true">
                  {channel.isEnabled ? 'On' : 'Off'}
                </span>
              </span>
            </li>
          );
        })}
      </ul>
    </div>
  );
}
