import { fireEvent, screen, waitFor, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { PERMISSIONS } from '@/access/permissions';
import { jsonResponse, requestLine } from '@/test/helpers';
import { ana, ben, cara, manager, openTeam as open, team } from '@/test/team';

const rowOf = (text: string) => screen.getByText(text).closest('tr') as HTMLElement;
const button = (name: string | RegExp) => screen.getByRole('button', { name });

describe('the team page: the table', () => {
  it('lists the people and the waiting invitations, each with a role, a status and a date', async () => {
    await open();

    expect(await screen.findByRole('heading', { level: 1, name: 'Team' })).toBeTruthy();
    expect(await screen.findByRole('heading', { level: 2, name: 'Employees' })).toBeTruthy();

    const person = within(rowOf('Ben Jones'));
    expect(person.getByText('ben@example.com')).toBeTruthy();
    expect((person.getByLabelText('Role of Ben Jones') as HTMLSelectElement).value).toBe('marketingmanager');
    expect(person.getByText('Active')).toBeTruthy();
    expect(person.getByText('Joined Mar 5, 2026')).toBeTruthy();

    const waiting = within(rowOf('dev@example.com'));
    expect(waiting.getByText('Content Writer')).toBeTruthy();
    expect(waiting.getByText('Invited')).toBeTruthy();
    expect(waiting.getByText('Expires Jan 15, 2030')).toBeTruthy();

    const expired = within(rowOf('old@example.com'));
    expect(expired.getByText('Expired')).toBeTruthy();
    expect(expired.getByText('Expired Oct 1, 2026')).toBeTruthy();
  });

  it('counts the employees and the invitations', async () => {
    await open();

    expect(await screen.findByText('3 employees, 2 invited')).toBeTruthy();
  });

  it('shows the person themselves with a You badge, and offers nothing to do to them', async () => {
    await open();
    await screen.findByText('Ben Jones');

    const you = within(rowOf('Ana Smith'));
    expect(you.getByText('You')).toBeTruthy();
    expect(you.queryByRole('combobox')).toBeNull();
    expect(you.queryByRole('button')).toBeNull();
    expect((you.getByRole('checkbox') as HTMLInputElement).disabled).toBe(true);
  });

  it('sorts the people by name and puts the invitations after them', async () => {
    await open(manager, team([cara, ana, ben]));
    await screen.findByText('Ben Jones');

    const names = screen.getAllByRole('row').slice(1).map((row) => row.querySelector('th')?.textContent ?? '');
    expect(names).toEqual(['Ana SmithYouana@example.com', 'Ben Jonesben@example.com', 'Cara Leecara@example.com', 'dev@example.com', 'old@example.com']);
  });

  it('says so when you are the only one, and points to the button', async () => {
    await open(manager, team([ana], []));

    expect(await screen.findByText(/only one here so far/)).toBeTruthy();
  });

  it('says so when the list cannot be loaded, and loads it on Try again', async () => {
    let failing = true;
    await open(manager, team(), { 'GET /v1/members': () => (failing ? jsonResponse(500, {}) : jsonResponse(200, team())) });

    expect((await screen.findByRole('alert')).textContent).toMatch(/team could not be loaded/);
    failing = false;
    fireEvent.click(button('Try again'));

    expect(await screen.findByText('Ben Jones')).toBeTruthy();
    expect(screen.queryByRole('alert')).toBeNull();
  });
});

describe('the team page: search and filters', () => {
  it('filters as you type, by name, address or role, in any letter case', async () => {
    await open();
    await screen.findByText('Ben Jones');
    const search = screen.getByRole('searchbox', { name: 'Search employees' });

    fireEvent.change(search, { target: { value: 'CARA' } });
    expect(screen.queryByText('Ben Jones')).toBeNull();
    expect(screen.getByText('Cara Lee')).toBeTruthy();
    expect(screen.getByText('Showing 1 of 5')).toBeTruthy();

    fireEvent.change(search, { target: { value: 'dev@' } });
    expect(screen.getByText('dev@example.com')).toBeTruthy();
    expect(screen.queryByText('Cara Lee')).toBeNull();

    fireEvent.change(search, { target: { value: 'marketing manager' } });
    expect(screen.getByText('Ben Jones')).toBeTruthy();
    expect(screen.getByText('old@example.com')).toBeTruthy();
    expect(screen.queryByText('Cara Lee')).toBeNull();
  });

  it('says when nothing matches and clears the search from there', async () => {
    await open();
    await screen.findByText('Ben Jones');

    fireEvent.change(screen.getByRole('searchbox'), { target: { value: 'zzz' } });
    expect(screen.getByText('No one matches your search or filters.')).toBeTruthy();
    expect(screen.queryByRole('table')).toBeNull();

    fireEvent.click(button('Clear search and filters'));
    expect(screen.getByText('Ben Jones')).toBeTruthy();
    expect((screen.getByRole('searchbox') as HTMLInputElement).value).toBe('');
  });

  it('adds a filter from the plus, as a chip, and a tap on the chip takes it off', async () => {
    await open();
    await screen.findByText('Ben Jones');

    fireEvent.click(button('Add a filter'));
    fireEvent.click(screen.getByRole('button', { name: 'role is Content Writer' }));

    const chip = button('Remove filter: role is Content Writer');
    expect(chip.textContent).toBe('role is Content Writer');
    expect(screen.queryByText('Ben Jones')).toBeNull();
    expect(screen.getByText('Cara Lee')).toBeTruthy();
    expect(screen.getByText('dev@example.com')).toBeTruthy();
    // The menu closed, and the filter it just added is no longer on offer.
    expect(screen.queryByRole('list', { name: 'Add a filter' })).toBeNull();
    fireEvent.click(button('Add a filter'));
    expect(screen.queryByRole('button', { name: 'role is Content Writer' })).toBeNull();

    fireEvent.click(chip);
    expect(screen.getByText('Ben Jones')).toBeTruthy();
  });

  it('treats filters of one kind as alternatives and the two kinds as both required', async () => {
    await open();
    await screen.findByText('Ben Jones');

    for (const name of ['role is Marketing Manager', 'role is Content Writer', 'status is Active']) {
      fireEvent.click(button('Add a filter'));
      fireEvent.click(screen.getByRole('button', { name }));
    }

    expect(screen.getByText('Ben Jones')).toBeTruthy();
    expect(screen.getByText('Cara Lee')).toBeTruthy();
    expect(screen.queryByText('Ana Smith')).toBeNull(); // active, but an organization admin
    expect(screen.queryByText('dev@example.com')).toBeNull(); // a content writer, but invited
  });

  it('offers only the roles this kind of organization allows', async () => {
    await open(manager, team());
    await screen.findByText('Ben Jones');
    fireEvent.click(button('Add a filter'));

    const menu = within(screen.getByRole('list', { name: 'Add a filter' }));
    expect(menu.queryByRole('button', { name: 'role is Client Manager' })).toBeNull();
    expect(menu.getByRole('button', { name: 'role is Organization Admin' })).toBeTruthy();
  });

  it('closes the filter menu on Escape and keeps the keyboard on the plus', async () => {
    await open();
    await screen.findByText('Ben Jones');
    fireEvent.click(button('Add a filter'));

    fireEvent.keyDown(screen.getByRole('list', { name: 'Add a filter' }), { key: 'Escape' });

    expect(screen.queryByRole('list', { name: 'Add a filter' })).toBeNull();
    await waitFor(() => expect(document.activeElement).toBe(button('Add a filter')));
  });
});

describe('the team page: who may use it', () => {
  it('never mounts for a person whose token lacks members.manage: no access, and no request for the team', async () => {
    const { fetchFn } = await open({ ...manager, permissions: [PERMISSIONS.dashboardView, PERMISSIONS.workspacesView] });

    expect(await screen.findByRole('heading', { level: 1, name: 'You do not have access to this page' })).toBeTruthy();
    expect(fetchFn.calls.map(requestLine)).not.toContain('GET /v1/members');
    expect(screen.queryByRole('button', { name: 'New Employee' })).toBeNull();
  });

  it('gives the Team link to the person who holds the permission', async () => {
    await open();
    await screen.findByText('Ben Jones');

    expect(screen.getByRole('link', { name: 'Team' }).getAttribute('href')).toBe('/team');
  });

  it('offers nobody to invite in an individual creator organization, even with the permission', async () => {
    await open({ ...manager, businessModel: 'creator', role: 'contentwriter' }, team([ana], []));

    expect(await screen.findByText(/works alone, so there is nobody to invite/)).toBeTruthy();
    expect(screen.queryByRole('button', { name: 'New Employee' })).toBeNull();
  });
});
