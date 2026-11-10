import { act, fireEvent, screen, waitFor, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { adminApp } from '@/navigation/apps';
import { jsonResponse, requestLine } from '@/test/helpers';
import { adminDashboardItem, adminServer, organization, organizationChannel, paged, renderApp } from '@/test/renderApp';

const usersItem = { id: 'a4', pageKey: 'users', label: 'Users', icon: 'users', path: '/admin/users' };
const items = [adminDashboardItem, usersItem];

const organizationsUrl = 'GET /v1/site/agencies?page=1&pageSize=10';
const channelsOf = (id: string) => `GET /v1/site/agencies/${id}/channels`;
const setChannel = (id: string, channelId: string) => `PUT /v1/site/agencies/${id}/channels/${channelId}`;
const statusOf = (id: string) => `PUT /v1/site/agencies/${id}/status`;

const northwind = organization('Northwind Foods', { id: 'o-1', businessModel: 'company', channels: 1 });
const acme = organization('Acme Ltd', { id: 'o-2', businessModel: 'agency', plan: 'paid', channels: 0 });
const cleo = organization('Cleo Makes', { id: 'o-3', businessModel: 'creator', channels: 0 });

const email = organizationChannel('Email', { description: 'Send email campaigns to contacts.', isEnabled: true, iconKey: 'email' });
const sms = organizationChannel('SMS', { iconKey: 'sms' });
const telegram = organizationChannel('Telegram', { hasIconImage: true, iconKey: null, description: 'Fast chat.' });

function open(overrides: Parameters<typeof adminServer>[1] = {}, rows: unknown[] = [northwind, acme, cleo], path = '/admin/users?tab=organizations') {
  return renderApp(
    path,
    adminServer(
      { items },
      {
        [organizationsUrl]: () => jsonResponse(200, paged(rows)),
        [channelsOf('o-1')]: () => jsonResponse(200, [email, sms, telegram]),
        [channelsOf('o-2')]: () => jsonResponse(200, [{ ...email, isEnabled: false }, sms]),
        [channelsOf('o-3')]: () => jsonResponse(200, [{ ...email, isEnabled: false }]),
        ...overrides,
      },
    ),
    adminApp,
  );
}

const cardOf = (name: string) => screen.getByRole('heading', { level: 3, name }).closest('li') as HTMLElement;
const toggleOf = (name: string) => within(cardOf(name)).getByRole('button', { name, expanded: false }) as HTMLButtonElement;
const fact = (card: HTMLElement, label: string) => within(card).getByText(label, { selector: 'dt' }).nextElementSibling?.textContent;
// By its label, not by role and name: a collapsed (hidden) region has no accessible name to find it by.
const regionOf = (name: string) => {
  const region = document.querySelector<HTMLElement>(`[role="region"][aria-label="Channels of ${name}"]`);
  if (!region) {
    throw new Error(`No channels region for ${name}`);
  }
  return region;
};
const calls = (fetchFn: { calls: Parameters<typeof requestLine>[0][] }) => fetchFn.calls.map(requestLine);

async function expand(name: string) {
  fireEvent.click(toggleOf(name));
  await within(regionOf(name)).findByRole('list');
}

/** A response the test answers by hand, so it can look at the page while a request is on its way. */
function deferred() {
  let resolve: (response: Response) => void = () => undefined;
  const promise = new Promise<Response>((done) => (resolve = done));
  return { promise, resolve };
}

describe('the organization cards', () => {
  it('show each organization with its kind, plan, members, workspaces, channels and date, and a Deactivate button', async () => {
    await open();

    expect(await screen.findByRole('heading', { level: 3, name: 'Northwind Foods' })).toBeTruthy();
    const card = cardOf('Northwind Foods');
    expect(within(card).getByText('Company')).toBeTruthy();
    expect(within(card).getByText('Trial')).toBeTruthy();
    expect(fact(card, 'Members')).toBe('2');
    expect(fact(card, 'Workspaces')).toBe('1');
    expect(fact(card, 'Channels')).toBe('1');
    expect(fact(card, 'Created')).toBe('Mar 5, 2026');
    expect(within(card).getByRole('button', { name: 'Deactivate Northwind Foods' })).toBeTruthy();
    expect(within(card).queryByText('Inactive')).toBeNull();
  });

  it('mark a deactivated organization Inactive, and offer Activate instead of Deactivate', async () => {
    await open({}, [organization('Gone Ltd', { id: 'o-9', isActive: false })]);

    const card = await screen.findByRole('heading', { level: 3, name: 'Gone Ltd' }).then(() => cardOf('Gone Ltd'));
    expect(within(card).getByText('Inactive')).toBeTruthy();
    expect(within(card).getByRole('button', { name: 'Activate Gone Ltd' })).toBeTruthy();
    expect(within(card).queryByRole('button', { name: /^Deactivate/ })).toBeNull();
    expect(card.className).toContain('is-inactive');
  });

  it('read no channels until one is opened', async () => {
    const { fetchFn } = await open();
    await screen.findByRole('heading', { level: 3, name: 'Northwind Foods' });

    expect(calls(fetchFn).filter((line) => line.includes('/channels'))).toEqual([]);
    for (const name of ['Northwind Foods', 'Acme Ltd', 'Cleo Makes']) {
      expect(toggleOf(name).getAttribute('aria-expanded')).toBe('false');
    }
  });
});

describe('opening an organization', () => {
  it('loads its channels once, shows a switch for each reflecting what is on, and keeps them when closed and opened again', async () => {
    const { fetchFn } = await open();
    await screen.findByRole('heading', { level: 3, name: 'Northwind Foods' });

    fireEvent.click(toggleOf('Northwind Foods'));

    const region = regionOf('Northwind Foods');
    expect(await within(region).findByRole('list')).toBeTruthy();
    expect(region.hasAttribute('hidden')).toBe(false);
    const button = within(cardOf('Northwind Foods')).getByRole('button', { name: 'Northwind Foods' });
    expect(button.getAttribute('aria-expanded')).toBe('true');
    expect(button.getAttribute('aria-controls')).toBe(region.id);
    expect(within(region).getAllByRole('switch').map((node) => (node as HTMLInputElement).checked)).toEqual([true, false, false]);
    expect(within(region).getByRole('switch', { name: 'Email' }).getAttribute('aria-describedby')).toBeTruthy();
    expect(within(region).getByText('Send email campaigns to contacts.')).toBeTruthy();
    expect(within(region).getByText('On')).toBeTruthy();
    expect(within(region).getAllByText('Off')).toHaveLength(2);
    expect(region.querySelector('img.channel-image')?.getAttribute('src')).toBe('/v1/site/channels/ch-telegram/icon');
    expect(region.querySelectorAll('svg.channel-icon')).toHaveLength(2);

    fireEvent.click(button);
    expect(regionOf('Northwind Foods').hasAttribute('hidden')).toBe(true);
    expect(button.getAttribute('aria-expanded')).toBe('false');

    fireEvent.click(button);
    expect(regionOf('Northwind Foods').hasAttribute('hidden')).toBe(false);
    expect(calls(fetchFn).filter((line) => line === channelsOf('o-1'))).toHaveLength(1);
  });

  it.each([
    ['a company', 'Northwind Foods', 'o-1', 3],
    ['a marketing agency', 'Acme Ltd', 'o-2', 2],
    ['an individual creator', 'Cleo Makes', 'o-3', 1],
  ])('works for %s: it reads that organization\'s own channels', async (_kind, name, id, count) => {
    const { fetchFn } = await open();
    await screen.findByRole('heading', { level: 3, name });

    await expand(name);

    expect(within(regionOf(name)).getAllByRole('switch')).toHaveLength(count);
    expect(calls(fetchFn).filter((line) => line.includes('/channels'))).toEqual([channelsOf(id)]);
  });

  it('keeps several organizations open at once, each with its own channels', async () => {
    await open();
    await screen.findByRole('heading', { level: 3, name: 'Northwind Foods' });

    await expand('Northwind Foods');
    await expand('Acme Ltd');

    expect(within(regionOf('Northwind Foods')).getAllByRole('switch')).toHaveLength(3);
    expect(within(regionOf('Acme Ltd')).getAllByRole('switch')).toHaveLength(2);
    expect(within(regionOf('Northwind Foods')).getByRole('switch', { name: 'Email' })).toHaveProperty('checked', true);
    expect(within(regionOf('Acme Ltd')).getByRole('switch', { name: 'Email' })).toHaveProperty('checked', false);
  });

  it('says it is loading while the channels are on their way', async () => {
    const pending = deferred();
    await open({ [channelsOf('o-1')]: () => pending.promise });
    await screen.findByRole('heading', { level: 3, name: 'Northwind Foods' });

    fireEvent.click(toggleOf('Northwind Foods'));

    expect(within(regionOf('Northwind Foods')).getByText('Loading channels…')).toBeTruthy();
    await act(async () => pending.resolve(jsonResponse(200, [email])));
    expect(await within(regionOf('Northwind Foods')).findByRole('switch', { name: 'Email' })).toBeTruthy();
  });

  it('says what went wrong, and reads again on Try again', async () => {
    let failing = true;
    const { fetchFn } = await open({ [channelsOf('o-1')]: () => (failing ? jsonResponse(500, {}) : jsonResponse(200, [email])) });
    await screen.findByRole('heading', { level: 3, name: 'Northwind Foods' });

    fireEvent.click(toggleOf('Northwind Foods'));

    expect((await within(regionOf('Northwind Foods')).findByRole('alert')).textContent).toMatch(/The channels could not be loaded\./);

    failing = false;
    fireEvent.click(within(regionOf('Northwind Foods')).getByRole('button', { name: 'Try again' }));

    expect(await within(regionOf('Northwind Foods')).findByRole('switch', { name: 'Email' })).toBeTruthy();
    expect(calls(fetchFn).filter((line) => line === channelsOf('o-1'))).toHaveLength(2);
  });

  it('says so when the organization is gone, and when the person is no longer a Site admin', async () => {
    await open({
      [channelsOf('o-1')]: () => jsonResponse(404, { code: 'organization_not_found' }),
      [channelsOf('o-2')]: () => jsonResponse(403, { code: 'not_site_admin' }),
    });
    await screen.findByRole('heading', { level: 3, name: 'Northwind Foods' });

    fireEvent.click(toggleOf('Northwind Foods'));
    expect((await within(regionOf('Northwind Foods')).findByRole('alert')).textContent).toMatch(/That organization no longer exists/);

    fireEvent.click(toggleOf('Acme Ltd'));
    expect((await within(regionOf('Acme Ltd')).findByRole('alert')).textContent).toMatch(/no longer a site admin/);
  });

  it('points to the Channels page when there are no channels at all', async () => {
    await open({ [channelsOf('o-1')]: () => jsonResponse(200, []) });
    await screen.findByRole('heading', { level: 3, name: 'Northwind Foods' });

    fireEvent.click(toggleOf('Northwind Foods'));

    const link = await within(regionOf('Northwind Foods')).findByRole('link', { name: 'Channels page' });
    expect(link.getAttribute('href')).toBe('/admin/channels');
  });
});

describe('the channel switches', () => {
  it('turn a channel on at once, save it with the choice, and bring the card\'s count with them', async () => {
    const pending = deferred();
    let body: unknown;
    const { fetchFn } = await open({
      [setChannel('o-1', 'ch-sms')]: (call) => {
        body = JSON.parse(call.init.body as string);
        return pending.promise;
      },
    });
    await screen.findByRole('heading', { level: 3, name: 'Northwind Foods' });
    await expand('Northwind Foods');
    const region = regionOf('Northwind Foods');
    const smsSwitch = within(region).getByRole('switch', { name: 'SMS' }) as HTMLInputElement;
    expect(fact(cardOf('Northwind Foods'), 'Channels')).toBe('1');

    fireEvent.click(smsSwitch);

    // The switch has moved already, is held still while the server answers, and the count follows it.
    expect(smsSwitch.checked).toBe(true);
    expect(smsSwitch.disabled).toBe(true);
    expect(fact(cardOf('Northwind Foods'), 'Channels')).toBe('2');
    expect((within(region).getByRole('switch', { name: 'Email' }) as HTMLInputElement).disabled).toBe(false);

    await act(async () => pending.resolve(new Response(null, { status: 204 })));

    await waitFor(() => expect(smsSwitch.disabled).toBe(false));
    expect(smsSwitch.checked).toBe(true);
    expect(body).toEqual({ enabled: true });
    expect(calls(fetchFn)).toContain(setChannel('o-1', 'ch-sms'));
    expect(fact(cardOf('Northwind Foods'), 'Channels')).toBe('2');
    expect(within(region).queryByRole('alert')).toBeNull();
  });

  it('turn a channel off, saving enabled: false, and the count goes down', async () => {
    let body: unknown;
    await open({
      [setChannel('o-1', 'ch-email')]: (call) => {
        body = JSON.parse(call.init.body as string);
        return new Response(null, { status: 204 });
      },
    });
    await screen.findByRole('heading', { level: 3, name: 'Northwind Foods' });
    await expand('Northwind Foods');

    fireEvent.click(within(regionOf('Northwind Foods')).getByRole('switch', { name: 'Email' }));

    await waitFor(() => expect(fact(cardOf('Northwind Foods'), 'Channels')).toBe('0'));
    expect(body).toEqual({ enabled: false });
    expect(within(regionOf('Northwind Foods')).getByRole('switch', { name: 'Email' })).toHaveProperty('checked', false);
  });

  it('put the switch and the count back, and say why, when the server refuses', async () => {
    await open({ [setChannel('o-1', 'ch-sms')]: () => jsonResponse(500, {}) });
    await screen.findByRole('heading', { level: 3, name: 'Northwind Foods' });
    await expand('Northwind Foods');
    const region = regionOf('Northwind Foods');

    fireEvent.click(within(region).getByRole('switch', { name: 'SMS' }));

    expect((await within(region).findByRole('alert')).textContent).toBe('SMS could not be changed. Try again in a moment.');
    expect(within(region).getByRole('switch', { name: 'SMS' })).toHaveProperty('checked', false);
    expect(within(region).getByRole('switch', { name: 'SMS' })).toHaveProperty('disabled', false);
    await waitFor(() => expect(fact(cardOf('Northwind Foods'), 'Channels')).toBe('1'));
  });

  it.each([
    [404, { code: 'channel_not_found' }, 'SMS no longer exists. Reload the list.'],
    [404, { code: 'organization_not_found' }, 'That organization no longer exists. Reload the list.'],
    [403, { code: 'not_site_admin' }, 'This account is no longer a site admin.'],
    [429, {}, 'Too many requests. Wait a minute, then try again.'],
  ])('explain a %s answer in words', async (status, body, message) => {
    await open({ [setChannel('o-1', 'ch-sms')]: () => jsonResponse(status, body) });
    await screen.findByRole('heading', { level: 3, name: 'Northwind Foods' });
    await expand('Northwind Foods');

    fireEvent.click(within(regionOf('Northwind Foods')).getByRole('switch', { name: 'SMS' }));

    expect((await within(regionOf('Northwind Foods')).findByRole('alert')).textContent).toBe(message);
    expect(within(regionOf('Northwind Foods')).getByRole('switch', { name: 'SMS' })).toHaveProperty('checked', false);
  });

  it('say the server could not be reached when the request fails outright, and clear the message on the next change', async () => {
    let failing = true;
    await open({
      [setChannel('o-1', 'ch-sms')]: () => {
        if (failing) {
          throw new TypeError('network down');
        }
        return new Response(null, { status: 204 });
      },
    });
    await screen.findByRole('heading', { level: 3, name: 'Northwind Foods' });
    await expand('Northwind Foods');
    const region = regionOf('Northwind Foods');

    fireEvent.click(within(region).getByRole('switch', { name: 'SMS' }));
    expect((await within(region).findByRole('alert')).textContent).toMatch(/Could not reach the server/);

    failing = false;
    fireEvent.click(within(region).getByRole('switch', { name: 'SMS' }));

    await waitFor(() => expect(within(region).queryByRole('alert')).toBeNull());
    expect(within(region).getByRole('switch', { name: 'SMS' })).toHaveProperty('checked', true);
  });

  it('change only the organization that was opened: the other cards\' counts stay as they were', async () => {
    await open();
    await screen.findByRole('heading', { level: 3, name: 'Acme Ltd' });
    await expand('Acme Ltd');
    await expand('Cleo Makes');

    fireEvent.click(within(regionOf('Acme Ltd')).getByRole('switch', { name: 'SMS' }));

    await waitFor(() => expect(fact(cardOf('Acme Ltd'), 'Channels')).toBe('1'));
    expect(fact(cardOf('Cleo Makes'), 'Channels')).toBe('0');
    expect(fact(cardOf('Northwind Foods'), 'Channels')).toBe('1');
  });
});

describe('deactivating and activating an organization', () => {
  it('asks first, says what happens, puts focus on the safe answer, and sends nothing until it is confirmed', async () => {
    const { fetchFn } = await open();
    await screen.findByRole('heading', { level: 3, name: 'Northwind Foods' });
    const card = cardOf('Northwind Foods');

    fireEvent.click(within(card).getByRole('button', { name: 'Deactivate Northwind Foods' }));

    const confirm = within(card).getByRole('group', { name: 'Confirm: deactivate Northwind Foods' });
    expect(within(confirm).getByText('Deactivate? Its members are signed out at once.')).toBeTruthy();
    expect(document.activeElement).toBe(within(confirm).getByRole('button', { name: 'Cancel' }));
    expect(within(card).queryByRole('button', { name: 'Deactivate Northwind Foods' })).toBeNull();
    expect(calls(fetchFn).filter((line) => line.startsWith('PUT'))).toEqual([]);

    fireEvent.click(within(confirm).getByRole('button', { name: 'Cancel' }));

    expect(within(card).queryByRole('group')).toBeNull();
    expect(document.activeElement).toBe(within(card).getByRole('button', { name: 'Deactivate Northwind Foods' }));
    expect(calls(fetchFn).filter((line) => line.startsWith('PUT'))).toEqual([]);
  });

  it('deactivates once confirmed: the card says Inactive, offers Activate with focus on it, and says what happened', async () => {
    let body: unknown;
    const { fetchFn } = await open({
      [statusOf('o-1')]: (call) => {
        body = JSON.parse(call.init.body as string);
        return new Response(null, { status: 204 });
      },
    });
    await screen.findByRole('heading', { level: 3, name: 'Northwind Foods' });
    const card = cardOf('Northwind Foods');
    fireEvent.click(within(card).getByRole('button', { name: 'Deactivate Northwind Foods' }));

    fireEvent.click(within(card).getByRole('button', { name: 'Yes, deactivate Northwind Foods' }));

    expect(await within(card).findByText('Inactive')).toBeTruthy();
    expect(body).toEqual({ isActive: false });
    expect(calls(fetchFn)).toContain(statusOf('o-1'));
    const activate = within(card).getByRole('button', { name: 'Activate Northwind Foods' });
    expect(document.activeElement).toBe(activate);
    expect(within(card).queryByRole('group')).toBeNull();
    const feedback = within(card).getByText(/was deactivated/);
    expect(feedback.getAttribute('role')).toBe('status');
    expect(feedback.textContent).toBe('Northwind Foods was deactivated. Its members were signed out and cannot sign in until you activate it.');
    expect(card.className).toContain('is-inactive');
  });

  it('activates in one tap, with no question, and the card is active again', async () => {
    let body: unknown;
    await open(
      {
        [statusOf('o-9')]: (call) => {
          body = JSON.parse(call.init.body as string);
          return new Response(null, { status: 204 });
        },
      },
      [organization('Gone Ltd', { id: 'o-9', isActive: false })],
    );
    await screen.findByRole('heading', { level: 3, name: 'Gone Ltd' });
    const card = cardOf('Gone Ltd');

    fireEvent.click(within(card).getByRole('button', { name: 'Activate Gone Ltd' }));

    expect(await within(card).findByRole('button', { name: 'Deactivate Gone Ltd' })).toBeTruthy();
    expect(body).toEqual({ isActive: true });
    expect(within(card).queryByText('Inactive')).toBeNull();
    expect(within(card).getByText('Gone Ltd is active again. Its members can sign in.')).toBeTruthy();
    expect(card.className).not.toContain('is-inactive');
  });

  it('holds the buttons still while the request is on its way', async () => {
    const pending = deferred();
    await open({ [statusOf('o-1')]: () => pending.promise });
    await screen.findByRole('heading', { level: 3, name: 'Northwind Foods' });
    const card = cardOf('Northwind Foods');
    fireEvent.click(within(card).getByRole('button', { name: 'Deactivate Northwind Foods' }));

    fireEvent.click(within(card).getByRole('button', { name: 'Yes, deactivate Northwind Foods' }));

    await waitFor(() => expect((within(card).getByRole('button', { name: 'Yes, deactivate Northwind Foods' }) as HTMLButtonElement).disabled).toBe(true));
    expect((within(card).getByRole('button', { name: 'Cancel' }) as HTMLButtonElement).disabled).toBe(true);

    await act(async () => pending.resolve(new Response(null, { status: 204 })));

    expect(await within(card).findByRole('button', { name: 'Activate Northwind Foods' })).toBeTruthy();
  });

  it.each([
    [500, {}, 'Northwind Foods could not be deactivated. Try again in a moment.'],
    [404, { code: 'organization_not_found' }, 'Northwind Foods no longer exists. Reload the list.'],
    [403, { code: 'not_site_admin' }, 'This account is no longer a site admin.'],
    [429, {}, 'Too many requests. Wait a minute, then try again.'],
  ])('leaves the organization as it was and says why on a %s answer', async (status, body, message) => {
    await open({ [statusOf('o-1')]: () => jsonResponse(status, body) });
    await screen.findByRole('heading', { level: 3, name: 'Northwind Foods' });
    const card = cardOf('Northwind Foods');
    fireEvent.click(within(card).getByRole('button', { name: 'Deactivate Northwind Foods' }));

    fireEvent.click(within(card).getByRole('button', { name: 'Yes, deactivate Northwind Foods' }));

    const alert = await within(card).findByRole('alert');
    expect(alert.textContent).toBe(message);
    expect(within(card).queryByText('Inactive')).toBeNull();
    expect(within(card).getByRole('button', { name: 'Deactivate Northwind Foods' })).toBeTruthy();
  });

  it('says so, and stays inactive, when activating fails', async () => {
    await open({ [statusOf('o-9')]: () => jsonResponse(500, {}) }, [organization('Gone Ltd', { id: 'o-9', isActive: false })]);
    await screen.findByRole('heading', { level: 3, name: 'Gone Ltd' });
    const card = cardOf('Gone Ltd');

    fireEvent.click(within(card).getByRole('button', { name: 'Activate Gone Ltd' }));

    expect((await within(card).findByRole('alert')).textContent).toBe('Gone Ltd could not be activated. Try again in a moment.');
    expect(within(card).getByText('Inactive')).toBeTruthy();
  });

  it('touches no other organization, and still lets the deactivated one open to its channels', async () => {
    await open({ [statusOf('o-1')]: () => new Response(null, { status: 204 }) });
    await screen.findByRole('heading', { level: 3, name: 'Northwind Foods' });
    const card = cardOf('Northwind Foods');
    fireEvent.click(within(card).getByRole('button', { name: 'Deactivate Northwind Foods' }));
    fireEvent.click(within(card).getByRole('button', { name: 'Yes, deactivate Northwind Foods' }));
    await within(card).findByText('Inactive');

    expect(within(cardOf('Acme Ltd')).queryByText('Inactive')).toBeNull();
    expect(within(cardOf('Cleo Makes')).queryByText('Inactive')).toBeNull();
    await expand('Northwind Foods');
    expect(within(regionOf('Northwind Foods')).getAllByRole('switch')).toHaveLength(3);
  });
});

describe('the lists of Users and the dashboard', () => {
  it('leave the Members list as it was', async () => {
    const { fetchFn } = await open({}, [northwind], '/admin/users');

    expect(await screen.findByRole('heading', { level: 3, name: 'Ana Smith' })).toBeTruthy();
    expect(screen.queryByRole('button', { name: /^Deactivate/ })).toBeNull();
    expect(calls(fetchFn).filter((line) => line.includes('/channels'))).toEqual([]);
  });

  it('keep the dashboard\'s list read-only, though it says which organizations are inactive', async () => {
    await renderApp(
      '/admin',
      adminServer({ items }, { 'GET /v1/site/agencies?page=1&pageSize=10': () => jsonResponse(200, paged([northwind, organization('Gone Ltd', { id: 'o-9', isActive: false })])) }),
      adminApp,
    );

    const gone = (await screen.findByRole('heading', { name: 'Gone Ltd' })).closest('li') as HTMLElement;
    expect(within(gone).getByText('Inactive')).toBeTruthy();
    expect(within(gone).queryByRole('button')).toBeNull();
    expect(within(screen.getByRole('heading', { name: 'Northwind Foods' }).closest('li') as HTMLElement).queryByText('Inactive')).toBeNull();
    expect(screen.queryByRole('button', { name: /Deactivate|Activate/ })).toBeNull();
  });
});
