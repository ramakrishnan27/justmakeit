// The sidebar icons. Each is a few strokes on a 24 by 24 grid. The names are the ones the server accepts (MenuIcons on
// the server); a test keeps the two lists equal. A name this client has no drawing for is shown as the plain circle, so
// a newer server never leaves a gap.

const shapes: Record<string, readonly string[]> = {
  home: ['M3 11.5 12 4l9 7.5', 'M5 10v10h14V10', 'M10 20v-6h4v6'],
  folder: ['M3 7a2 2 0 0 1 2-2h4l2 2h8a2 2 0 0 1 2 2v8a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z'],
  menu: ['M4 6h16', 'M4 12h16', 'M4 18h16'],
  shield: ['M12 3 5 6v5c0 4.5 3 8 7 10 4-2 7-5.5 7-10V6z', 'm9 12 2 2 4-4'],
  users: ['M16 20v-1.5a3.5 3.5 0 0 0-3.5-3.5h-5A3.5 3.5 0 0 0 4 18.5V20', 'M10 11a3.5 3.5 0 1 0 0-7 3.5 3.5 0 0 0 0 7z', 'M20 20v-1.5a3.5 3.5 0 0 0-2.5-3.3', 'M15.5 4.2a3.5 3.5 0 0 1 0 6.6'],
  mail: ['M4 5h16a1 1 0 0 1 1 1v12a1 1 0 0 1-1 1H4a1 1 0 0 1-1-1V6a1 1 0 0 1 1-1z', 'm3 7 9 6 9-6'],
  chart: ['M5 20v-8', 'M12 20V5', 'M19 20v-5'],
  settings: ['M4 7h9', 'M17 7h3', 'M13 5v4h4V5z', 'M4 17h3', 'M11 17h9', 'M7 15v4h4v-4z'],
  list: ['M9 6h11', 'M9 12h11', 'M9 18h11', 'M4 6h.01', 'M4 12h.01', 'M4 18h.01'],
  circle: ['M12 4a8 8 0 1 0 0 16 8 8 0 0 0 0-16z'],
};

/** Every icon this client can draw. */
export const iconNames: readonly string[] = Object.keys(shapes);

/** The strokes of an icon, as SVG path data. */
export function iconPaths(name: string): readonly string[] {
  return shapes[resolveIcon(name)] ?? [];
}

/** The icon to draw for a name from the server: the name itself when it is known, otherwise the circle. */
export function resolveIcon(name: unknown): string {
  return typeof name === 'string' && Object.hasOwn(shapes, name) ? name : 'circle';
}
