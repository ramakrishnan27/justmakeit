import { iconPaths } from './icons';

/** An icon, hidden from assistive technology: the link or button beside it carries the words. */
export function Icon({ name }: { name: string }) {
  return (
    <svg className="icon" viewBox="0 0 24 24" aria-hidden="true" focusable="false">
      {iconPaths(name).map((d) => (
        <path key={d} d={d} />
      ))}
    </svg>
  );
}
