// The few drawings the screens themselves use (a magnifier, a plus, a cross). They are not menu icons: the server never
// accepts these names for a menu item, so they live apart from icons.ts, whose list a test keeps equal to the server's.

const shapes = {
  search: ['M11 4a7 7 0 1 0 0 14 7 7 0 0 0 0-14z', 'm20 20-3.5-3.5'],
  plus: ['M12 5v14', 'M5 12h14'],
  close: ['M6 6l12 12', 'M18 6 6 18'],
  chevron: ['m6 9 6 6 6-6'],
  upload: ['M12 16V4', 'm7 9 5-5 5 5', 'M5 20h14'],
  download: ['M12 4v12', 'm7 11 5 5 5-5', 'M5 20h14'],
  folder: ['M3 7a2 2 0 0 1 2-2h4l2 2h8a2 2 0 0 1 2 2v8a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z'],
  folderPlus: ['M3 7a2 2 0 0 1 2-2h4l2 2h8a2 2 0 0 1 2 2v8a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z', 'M12 10v5', 'M9.5 12.5h5'],
  grid: ['M4 4h6v6H4z', 'M14 4h6v6h-6z', 'M4 14h6v6H4z', 'M14 14h6v6h-6z'],
  rows: ['M8 6h12', 'M8 12h12', 'M8 18h12', 'M4 6h.01', 'M4 12h.01', 'M4 18h.01'],
  trash: ['M4 7h16', 'M10 11v6', 'M14 11v6', 'M6 7l1 13h10l1-13', 'M9 7V4h6v3'],
  more: ['M12 5h.01', 'M12 12h.01', 'M12 19h.01'],
  play: ['M8 5v14l11-7z'],
  file: ['M7 3h7l5 5v13H7z', 'M14 3v5h5', 'M10 13h6', 'M10 17h6'],
  video: ['M4 6h11a1 1 0 0 1 1 1v10a1 1 0 0 1-1 1H4a1 1 0 0 1-1-1V7a1 1 0 0 1 1-1z', 'm16 10 5-3v10l-5-3'],
  music: ['M9 18V6l10-2v12', 'M9 18a3 3 0 1 1-6 0 3 3 0 0 1 6 0z', 'M19 16a3 3 0 1 1-6 0 3 3 0 0 1 6 0z'],
} as const;

export type UiIconName = keyof typeof shapes;

/** A drawing for a button or field, hidden from assistive technology: the control beside it carries the words. */
export function UiIcon({ name }: { name: UiIconName }) {
  return (
    <svg className="icon" viewBox="0 0 24 24" aria-hidden="true" focusable="false">
      {shapes[name].map((d) => (
        <path key={d} d={d} />
      ))}
    </svg>
  );
}
