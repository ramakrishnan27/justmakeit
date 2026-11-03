import { fireEvent, render, screen, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it, vi } from 'vitest';
import type { MenuItem } from '@/navigation/menu';
import { Menu } from './Menu';

const dashboard: MenuItem = { id: 'm1', pageKey: 'dashboard', label: 'Dashboard', icon: 'home', path: '/dashboard' };
const workspaces: MenuItem = { id: 'm2', pageKey: 'workspaces', label: 'Workspaces', icon: 'folder', path: '/workspaces' };

function renderMenu(props: Partial<React.ComponentProps<typeof Menu>> = {}, at = '/dashboard') {
  return render(
    <MemoryRouter initialEntries={[at]}>
      <Menu label="Main" items={[dashboard, workspaces]} {...props} />
    </MemoryRouter>,
  );
}

describe('<Menu>', () => {
  it('is a navigation landmark named by its label', () => {
    renderMenu({ label: 'Main' });

    expect(screen.getByRole('navigation', { name: 'Main' })).toBeTruthy();
  });

  it('draws one link for each item, in the order given, to the address of the item', () => {
    renderMenu();

    const links = within(screen.getByRole('navigation')).getAllByRole('link');
    expect(links.map((link) => link.textContent)).toEqual(['Dashboard', 'Workspaces']);
    expect(links.map((link) => link.getAttribute('href'))).toEqual(['/dashboard', '/workspaces']);
  });

  it('draws the icon of each item, hidden from assistive technology', () => {
    renderMenu();

    const icons = screen.getByRole('navigation').querySelectorAll('svg.icon');
    expect(icons).toHaveLength(2);
    for (const icon of icons) {
      expect(icon.getAttribute('aria-hidden')).toBe('true');
    }
  });

  it('marks the link of the open page, and only that one', () => {
    renderMenu({}, '/workspaces');

    expect(screen.getByRole('link', { name: 'Workspaces' }).getAttribute('aria-current')).toBe('page');
    expect(screen.getByRole('link', { name: 'Dashboard' }).getAttribute('aria-current')).toBeNull();
  });

  it('tells its parent when a link is chosen, once for each choice', () => {
    const onNavigate = vi.fn();
    renderMenu({ onNavigate });

    fireEvent.click(screen.getByRole('link', { name: 'Workspaces' }));
    expect(onNavigate).toHaveBeenCalledTimes(1);

    fireEvent.click(screen.getByRole('link', { name: 'Dashboard' }));
    expect(onNavigate).toHaveBeenCalledTimes(2);
  });

  it('moves to the page of the link that was chosen', () => {
    renderMenu();

    fireEvent.click(screen.getByRole('link', { name: 'Workspaces' }));

    expect(screen.getByRole('link', { name: 'Workspaces' }).getAttribute('aria-current')).toBe('page');
  });

  it('says so when there is nothing to show, and draws no link', () => {
    renderMenu({ items: [] });

    expect(screen.getByText('No pages are available to you yet.')).toBeTruthy();
    expect(screen.queryByRole('link')).toBeNull();
  });

  it('can say it in other words', () => {
    renderMenu({ items: [], emptyText: 'Nothing here.' });

    expect(screen.getByText('Nothing here.')).toBeTruthy();
  });

  it('draws the circle for an icon it does not know, so a newer server never leaves a gap', () => {
    renderMenu({ items: [{ ...dashboard, icon: 'sparkles' }] });

    expect(screen.getByRole('navigation').querySelectorAll('svg.icon path')).toHaveLength(1);
  });
});
