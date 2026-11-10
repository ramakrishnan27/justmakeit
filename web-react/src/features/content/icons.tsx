// The drawings Content Studio uses beyond the sidebar's: one for each kind of content and one for each platform. Simple outlines in
// the same line style as the rest of the app. The platform ones suggest each service; they are not its official logo, so no brand
// assets are needed and they take the colours of the page.

import type { ContentType, SocialPlatform } from '@/api/content';
import { channelIconPaths } from '@/components/icons/channelIcons';
import { platformName } from '@/lib/content';

const typeShapes: Record<ContentType, readonly string[]> = {
  social_post: ['M4 5h16a1 1 0 0 1 1 1v10a1 1 0 0 1-1 1H9l-4 4v-4H4a1 1 0 0 1-1-1V6a1 1 0 0 1 1-1z', 'M8 11h.01', 'M12 11h.01', 'M16 11h.01'],
  blog_post: ['M7 3h8l4 4v14H7z', 'M15 3v4h4', 'M10 12h6', 'M10 16h6'],
  video_script: ['M5 4.5v15l14-7.5z'],
  carousel: ['M4 4h7v7H4z', 'M13 4h7v7h-7z', 'M4 13h7v7H4z', 'M13 13h7v7h-7z'],
  short_reel: ['M13 3 5 13h6l-1 8 8-10h-6z'],
};

/** The drawing of a kind of content, hidden from assistive technology: the name beside it carries the words. */
export function ContentTypeIcon({ type }: { type: ContentType }) {
  return (
    <svg className="icon" viewBox="0 0 24 24" aria-hidden="true" focusable="false">
      {(typeShapes[type] ?? typeShapes.social_post).map((d) => (
        <path key={d} d={d} />
      ))}
    </svg>
  );
}

const platformShapes: Record<SocialPlatform, readonly string[]> = {
  instagram: channelIconPaths('instagram'),
  facebook: channelIconPaths('facebook'),
  youtube: channelIconPaths('youtube'),
  linkedin: channelIconPaths('linkedin'),
  x: ['M4 4l16 16', 'M20 4 4 20'],
  tiktok: ['M14 3v11.5a3.5 3.5 0 1 1-3.5-3.5', 'M14 3c.4 2.4 2 4 5 4.2'],
  pinterest: ['M12 3a9 9 0 1 0 0 18 9 9 0 0 0 0-18z', 'M10.5 17.5 12 10', 'M10 9.5a2.5 2.5 0 1 1 3.5 2.3'],
  threads: ['M12 3a9 9 0 1 0 0 18 9 9 0 0 0 9-9', 'M16 12a4 4 0 1 1-4-4c2.2 0 4 1.5 4 4v1.2a1.8 1.8 0 0 0 3.6 0'],
};

/**
 * The drawing of a platform. Named (`role="img"`) by default, for a place where the icon is all there is, such as the list's
 * Platforms column; `decorative` hides it where the platform's name is written beside it.
 */
export function PlatformIcon({ platform, decorative = false }: { platform: string; decorative?: boolean }) {
  const shapes = Object.hasOwn(platformShapes, platform) ? platformShapes[platform as SocialPlatform] : channelIconPaths('broadcast');
  const name = platformName(platform);
  return (
    <svg
      className="icon platform-icon"
      viewBox="0 0 24 24"
      focusable="false"
      {...(decorative ? { 'aria-hidden': true } : { role: 'img', 'aria-label': name })}
    >
      {!decorative ? <title>{name}</title> : null}
      {shapes.map((d) => (
        <path key={d} d={d} />
      ))}
    </svg>
  );
}
