// The sidebar icons. Each is a few strokes on a 24 by 24 grid, drawn with DOM calls (never markup from a string).
// The names are the ones the server accepts (MenuIcons on the server); a test keeps the two lists equal. A name this
// client has no drawing for is shown as the plain circle, so a newer server never leaves a gap.

const shapes = {
  home: ['M3 11.5 12 4l9 7.5', 'M5 10v10h14V10', 'M10 20v-6h4v6'],
  folder: ['M3 7a2 2 0 0 1 2-2h4l2 2h8a2 2 0 0 1 2 2v8a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z'],
  menu: ['M4 6h16', 'M4 12h16', 'M4 18h16'],
  shield: ['M12 3 5 6v5c0 4.5 3 8 7 10 4-2 7-5.5 7-10V6z', 'm9 12 2 2 4-4'],
  users: ['M16 20v-1.5a3.5 3.5 0 0 0-3.5-3.5h-5A3.5 3.5 0 0 0 4 18.5V20', 'M10 11a3.5 3.5 0 1 0 0-7 3.5 3.5 0 0 0 0 7z', 'M20 20v-1.5a3.5 3.5 0 0 0-2.5-3.3', 'M15.5 4.2a3.5 3.5 0 0 1 0 6.6'],
  mail: ['M4 5h16a1 1 0 0 1 1 1v12a1 1 0 0 1-1 1H4a1 1 0 0 1-1-1V6a1 1 0 0 1 1-1z', 'm3 7 9 6 9-6'],
  chart: ['M5 20v-8', 'M12 20V5', 'M19 20v-5'],
  settings: ['M4 7h9', 'M17 7h3', 'M13 5v4h4V5z', 'M4 17h3', 'M11 17h9', 'M7 15v4h4v-4z'],
  list: ['M9 6h11', 'M9 12h11', 'M9 18h11', 'M4 6h.01', 'M4 12h.01', 'M4 18h.01'],
  edit: ['M5 19l1-4L17 4l3 3L9 18z', 'M14 7l3 3'],
  image: ['M5 4h14a1 1 0 0 1 1 1v14a1 1 0 0 1-1 1H5a1 1 0 0 1-1-1V5a1 1 0 0 1 1-1z', 'M9 10a1.5 1.5 0 1 0 0-3 1.5 1.5 0 0 0 0 3z', 'm4 17 5-5 4 4 3-3 4 4'],
  sparkles: ['M12 3l1.8 4.7L18.5 9.5l-4.7 1.8L12 16l-1.8-4.7L5.5 9.5l4.7-1.8z', 'M19 15v4', 'M17 17h4', 'M5 17v3', 'M3.5 18.5h3'],
  calendar: ['M5 5h14a1 1 0 0 1 1 1v13a1 1 0 0 1-1 1H5a1 1 0 0 1-1-1V6a1 1 0 0 1 1-1z', 'M4 10h16', 'M8 3v4', 'M16 3v4'],
  circle: ['M12 4a8 8 0 1 0 0 16 8 8 0 0 0 0-16z'],
};

/** Every icon this client can draw, in the order the Site admin's editor offers them. */
export const iconNames = Object.keys(shapes);

/** The icon to draw for a name from the server: the name itself when it is known, otherwise the circle. */
export function resolveIcon(name) {
  return typeof name === 'string' && Object.hasOwn(shapes, name) ? name : 'circle';
}

/** An icon name as words for a list of choices: "chart" becomes "Chart". */
export function iconLabel(name) {
  const text = typeof name === 'string' ? name : '';
  return text.length === 0 ? '–' : text.charAt(0).toUpperCase() + text.slice(1);
}

const svgNamespace = 'http://www.w3.org/2000/svg';

/** An <svg> for the icon, hidden from assistive technology: the link or button beside it carries the words. */
export function createIcon(name) {
  const svg = document.createElementNS(svgNamespace, 'svg');
  svg.setAttribute('viewBox', '0 0 24 24');
  svg.setAttribute('class', 'icon');
  svg.setAttribute('aria-hidden', 'true');
  svg.setAttribute('focusable', 'false');

  for (const d of shapes[resolveIcon(name)]) {
    const path = document.createElementNS(svgNamespace, 'path');
    path.setAttribute('d', d);
    svg.append(path);
  }
  return svg;
}
