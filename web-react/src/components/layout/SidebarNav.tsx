import { NavLink } from 'react-router-dom';
import { Icon } from '@/components/icons/Icon';
import type { MenuItem } from '@/navigation/menu';

/**
 * The sidebar links the server built for this person (the session holds them in memory; nothing is asked of the server
 * here). The link of the open page is marked with aria-current="page".
 */
export function SidebarNav({ items }: { items: readonly MenuItem[] }) {
  if (items.length === 0) {
    return (
      <ul className="nav-list">
        <li className="nav-empty muted small">No pages are available to you yet.</li>
      </ul>
    );
  }

  return (
    <ul className="nav-list">
      {items.map((item) => (
        <li key={item.id}>
          <NavLink className="nav-link" to={item.path} end>
            <Icon name={item.icon} />
            <span>{item.label}</span>
          </NavLink>
        </li>
      ))}
    </ul>
  );
}
