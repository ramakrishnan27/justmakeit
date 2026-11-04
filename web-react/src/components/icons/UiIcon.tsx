// The few drawings the screens themselves use (a magnifier, a plus, a cross). They are not menu icons: the server never
// accepts these names for a menu item, so they live apart from icons.ts, whose list a test keeps equal to the server's.

const shapes = {
  search: ['M11 4a7 7 0 1 0 0 14 7 7 0 0 0 0-14z', 'm20 20-3.5-3.5'],
  plus: ['M12 5v14', 'M5 12h14'],
  close: ['M6 6l12 12', 'M18 6 6 18'],
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
