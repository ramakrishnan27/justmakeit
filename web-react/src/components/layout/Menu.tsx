import { Link, useLocation } from 'react-router-dom';
import { Icon } from '@/components/icons/Icon';
import { isCurrentPath, type MenuItem } from '@/navigation/menu';

function MenuLink({ item, current, onNavigate }: { item: MenuItem; current: boolean; onNavigate?: () => void }) {
  return (
    <Link className="nav-link" to={item.path} aria-current={current ? 'page' : undefined} onClick={onNavigate}>
      <Icon name={item.icon} />
      <span>{item.label}</span>
    </Link>
  );
}

/**
 * A navigation menu: one link for each item, with the link of the open page marked (aria-current="page"). A page below an item's
 * address (the Content Studio editor) marks that item too. It is the whole navigation landmark, so a screen reader finds it by its
 * `label`.
 *
 * The items are the ones the server built for this person (the session holds them in memory), already checked by
 * `parseMenu`, so this never draws a link that could leave the site. `onNavigate` runs when a link is chosen, for a
 * parent that must react (the drawer closes).
 *
 * Only the app shell uses it, and the shell is drawn only behind sign-in: the sign-in and sign-up pages, and any other
 * bare page, never show a menu.
 */
export function Menu({
  items,
  label,
  onNavigate,
  emptyText = 'No pages are available to you yet.',
}: {
  items: readonly MenuItem[];
  label: string;
  onNavigate?: () => void;
  emptyText?: string;
}) {
  const { pathname } = useLocation();
  const paths = items.map((item) => item.path);

  return (
    <nav aria-label={label}>
      <ul className="nav-list">
        {items.length === 0 ? (
          <li className="nav-empty muted small">{emptyText}</li>
        ) : (
          items.map((item) => (
            <li key={item.id}>
              <MenuLink item={item} current={isCurrentPath(item.path, pathname, paths)} onNavigate={onNavigate} />
            </li>
          ))
        )}
      </ul>
    </nav>
  );
}
