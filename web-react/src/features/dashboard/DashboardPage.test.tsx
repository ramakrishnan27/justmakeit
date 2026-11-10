import { screen, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { formatDate } from '@/lib/format';
import { memberServer, renderApp, sessionEndsAt, type MemberOptions } from '@/test/renderApp';

const open = (options: MemberOptions = {}) => renderApp('/dashboard', memberServer(options));
const section = (name: string) => screen.getByRole('region', { name });

describe('the dashboard', () => {
  it('greets the person by first name and says what they are at which organization', async () => {
    await open({ role: 'organizationadmin', permissions: ['dashboard.view'], fullName: 'Ana Maria Smith' });

    expect(await screen.findByRole('heading', { level: 1, name: 'Welcome, Ana' })).toBeTruthy();
    expect(screen.getByText('Organization Admin at Northwind Foods')).toBeTruthy();
  });

  it('falls back to the email when the person has no name', async () => {
    await open({ fullName: '' });

    expect(await screen.findByRole('heading', { level: 1, name: 'Welcome, ana@example.com' })).toBeTruthy();
  });

  it('describes the role for the kind of organization', async () => {
    await open({ businessModel: 'agency', role: 'clientmanager' });
    await screen.findByRole('heading', { level: 1 });

    expect(within(section('Your role')).getByText('Client Manager')).toBeTruthy();
    expect(within(section('Your role')).getByText(/client relationships/)).toBeTruthy();
  });

  it('lists what is still to come for the role, labelled as not built', async () => {
    await open({ role: 'contentwriter' });
    await screen.findByRole('heading', { level: 1 });

    const soon = section('Coming soon for your role');
    expect(within(soon).getByText('Content ideas and writing')).toBeTruthy();
    expect(within(soon).getAllByText('Coming soon').length).toBeGreaterThan(0);
  });

  it('says Yes for what the token allows and "Not for your role" for what it does not', async () => {
    await open({ permissions: ['dashboard.view', 'data.read', 'data.write'] });
    await screen.findByRole('heading', { level: 1 });

    const abilities = within(section('What you can do'));
    const row = (text: string) => abilities.getByText(text).closest('li')!;
    expect(within(row('View the products and everything in them')).getByText('Yes')).toBeTruthy();
    expect(within(row('Create and change content')).getByText('Yes')).toBeTruthy();
    expect(within(row('Add products and manage the team')).getByText('Not for your role')).toBeTruthy();
  });

  it('lists the permissions of the token and the claims it was made with', async () => {
    await open({ role: 'marketingmanager', permissions: ['dashboard.view', 'data.read'] });
    await screen.findByRole('heading', { level: 1 });

    const session = within(section('Your session'));
    expect(session.getByText('dashboard.view')).toBeTruthy();
    expect(session.getByText('data.read')).toBeTruthy();
    expect(session.getByText('u2')).toBeTruthy();
    expect(session.getByText('marketingmanager')).toBeTruthy();
    expect(session.getByText('Trial')).toBeTruthy();
  });

  it('says until when the sign-in lasts, and that it is not renewed', async () => {
    await open();
    await screen.findByRole('heading', { level: 1 });

    const session = within(section('Your session'));
    expect(session.getByText('Signed in until')).toBeTruthy();
    expect(session.getByText(formatDate(sessionEndsAt, { withTime: true }))).toBeTruthy();
    expect(session.getByText(/not renewed/)).toBeTruthy();
    expect(session.queryByText(/Token expires/)).toBeNull();
  });
});
