// The built-in channel icons: a few strokes on a 24 by 24 grid, in the same line style as the sidebar icons (icons.ts).
// They are simple outlines that suggest each service, not its official logo, so they take the colours of the page and need
// no brand assets. The seven built-in channels point at them by name (`icon_key` in 011_channels.sql, and a test keeps the two
// in step), and so does a channel made with no uploaded image (it gets `broadcast`, the server's ChannelIconRules.DefaultGlyph).
// Kept apart from the menu icons on purpose: the server accepts a fixed list of those (MenuIcons), and a channel is not a menu item.

import { iconPaths } from './icons';

const shapes: Record<string, readonly string[]> = {
  instagram: ['M7 3h10a4 4 0 0 1 4 4v10a4 4 0 0 1-4 4H7a4 4 0 0 1-4-4V7a4 4 0 0 1 4-4z', 'M12 8.5a3.5 3.5 0 1 0 0 7 3.5 3.5 0 0 0 0-7z', 'M17.5 6.5h.01'],
  facebook: ['M18 2h-3a5 5 0 0 0-5 5v3H7v4h3v8h4v-8h3l1-4h-4V7a1 1 0 0 1 1-1h3z'],
  youtube: ['M4 5.5h16a2 2 0 0 1 2 2v9a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2v-9a2 2 0 0 1 2-2z', 'M10 9.5v5l4.5-2.5z'],
  email: iconPaths('mail'),
  sms: ['M4 5h16a1 1 0 0 1 1 1v10a1 1 0 0 1-1 1H9l-4 4v-4H4a1 1 0 0 1-1-1V6a1 1 0 0 1 1-1z', 'M8 11h.01', 'M12 11h.01', 'M16 11h.01'],
  whatsapp: ['M3 21l1.7-5A8.5 8.5 0 1 1 8 19.3z', 'M9 8.5c.2 3.3 2.7 5.8 6 6l1-1.4-1.9-1-.9.8c-.9-.4-1.6-1.1-2-2l.8-.9-1-1.9z'],
  linkedin: ['M5 3h14a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2z', 'M8 10.5V17', 'M8 7.5h.01', 'M12 10.5V17', 'M12 13.5a3 3 0 0 1 6 0V17'],
  // Radio waves: what a channel with no picture of its own is drawn as.
  broadcast: ['M12 11a1 1 0 1 0 0 2 1 1 0 0 0 0-2z', 'M8.5 15.5a5 5 0 0 1 0-7', 'M15.5 8.5a5 5 0 0 1 0 7', 'M5.6 18.4a9 9 0 0 1 0-12.8', 'M18.4 5.6a9 9 0 0 1 0 12.8'],
};

/** Every built-in channel icon this client can draw. */
export const channelGlyphNames: readonly string[] = Object.keys(shapes);

/** The strokes of a built-in channel icon, as SVG path data. A name this client has no drawing for gets the plain circle. */
export function channelIconPaths(key: string | null | undefined): readonly string[] {
  return typeof key === 'string' && Object.hasOwn(shapes, key) ? (shapes[key] ?? iconPaths('circle')) : iconPaths('circle');
}
