import { useState } from 'react';
import type { SiteChannel } from '@/api/types';
import { channelIconUrl } from '@/lib/channelForm';
import { channelIconPaths } from './channelIcons';

/** The part of a channel its icon is drawn from. A channel in an organization's list has the same three fields. */
export type ChannelIconSource = Pick<SiteChannel, 'id' | 'iconKey' | 'hasIconImage'>;

/**
 * A channel's icon, hidden from assistive technology: the name beside it carries the words. An uploaded image is drawn from
 * the address the server serves it at (the Site admin's by default, `iconSrc` for a member's page); a built-in icon (or an image that cannot be loaded) is drawn from its strokes.
 */
export function ChannelIcon({ channel, iconSrc }: { channel: ChannelIconSource; iconSrc?: string }) {
  const [broken, setBroken] = useState(false);

  if (channel.hasIconImage && !broken) {
    return (
      <img
        className="channel-image"
        src={iconSrc ?? channelIconUrl(channel.id)}
        alt=""
        width={32}
        height={32}
        loading="lazy"
        decoding="async"
        onError={() => setBroken(true)}
      />
    );
  }

  return (
    <svg className="icon channel-icon" viewBox="0 0 24 24" aria-hidden="true" focusable="false">
      {channelIconPaths(channel.iconKey).map((d) => (
        <path key={d} d={d} />
      ))}
    </svg>
  );
}
