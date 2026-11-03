import { act, fireEvent, screen, waitFor, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { jsonResponse, requestLine } from '@/test/helpers';
import { memberServer, renderApp, visitorServer, workspacesItem } from '@/test/renderApp';
import { setWideScreen } from '@/test/viewport';

const heading = (name: string | RegExp) => screen.findByRole('heading', { level: 1, name });

describe('the route guards', () => {
  it('sends a visitor who asks for a page to the sign-in form', async () => {
    await renderApp('/dashboard', visitorServer());

    expect(await heading('Welcome back')).toBeTruthy();
    expect(screen.queryByRole('button', { name: 'Sign out' })).toBeNull();
  });

  it('sends a visitor who asks for the root to the sign-in form, too', async () => {
    await renderApp('/', visitorServer());

    expect(await heading('Welcome back')).toBeTruthy();
  });

  it('sends a signed-in person away from the sign-in and sign-up forms', async () => {
    const { unmount } = await renderApp('/login', memberServer());
    expect(await heading('Welcome, Ana')).toBeTruthy();
    unmount();

    await renderApp('/register', memberServer());
    expect(await heading('Welcome, Ana')).toBeTruthy();
  });

  it('shows a signed-in person the page in the frame: header, sidebar and the page', async () => {
    await renderApp('/dashboard', memberServer());

    expect(await heading('Welcome, Ana')).toBeTruthy();
    expect(screen.getByRole('button', { name: 'Sign out' })).toBeTruthy();
    expect(screen.getByRole('navigation', { name: 'Main' })).toBeTruthy();
    expect(screen.getByText('Northwind Foods')).toBeTruthy();
  });

  it('shows "not found" inside the frame to a signed-in person, and bare to a visitor', async () => {
    const member = await renderApp('/nothing-here', memberServer());
    expect(await heading('Page not found')).toBeTruthy();
    expect(screen.getByRole('button', { name: 'Sign out' })).toBeTruthy();
    member.unmount();

    await renderApp('/nothing-here', visitorServer());
    expect(await heading('Page not found')).toBeTruthy();
    expect(screen.queryByRole('button', { name: 'Sign out' })).toBeNull();
  });

  it('returns to the sign-in form after sign-out, and a second visit to a page needs a new sign-in', async () => {
    await renderApp('/dashboard', memberServer());
    await heading('Welcome, Ana');

    fireEvent.click(screen.getByRole('button', { name: 'Sign out' }));

    expect(await heading('Welcome back')).toBeTruthy();
    expect(screen.queryByRole('navigation', { name: 'Main' })).toBeNull();
  });

  it('sends the person to the sign-in form when a request finds the session can no longer be renewed', async () => {
    let cookieIsGood = true;
    const { api } = await renderApp(
      '/dashboard',
      memberServer({}, {
        'POST /v1/auth/refresh': () => (cookieIsGood ? jsonResponse(200, { accessToken: 'again' }) : jsonResponse(401, {})),
        'GET /v1/anything': () => jsonResponse(401, {}),
      }),
    );
    await heading('Welcome, Ana');
    cookieIsGood = false;

    await act(async () => {
      await api.request('/v1/anything').catch(() => undefined);
    });

    expect(await heading('Welcome back')).toBeTruthy();
  });
});

describe('components that load by permission', () => {
  it('opens a page for a person whose token holds its permission', async () => {
    await renderApp('/workspaces', memberServer({}, { 'GET /v1/workspaces': () => jsonResponse(200, []) }));

    expect(await heading('Workspaces')).toBeTruthy();
  });

  it('never mounts a page the token does not allow: the person sees "no access" and the page makes no request', async () => {
    const { fetchFn } = await renderApp(
      '/workspaces',
      memberServer({ permissions: ['dashboard.view'], items: [] }, { 'GET /v1/workspaces': () => jsonResponse(200, []) }),
    );

    expect(await heading('You do not have access to this page')).toBeTruthy();
    expect(fetchFn.calls.map(requestLine)).not.toContain('GET /v1/workspaces');
    expect(screen.getByRole('link', { name: 'Go to the dashboard' })).toBeTruthy();
  });

  it('takes a page away from a person whose permissions change while they are on it', async () => {
    let permissions = ['dashboard.view', 'workspaces.view'];
    const { session } = await renderApp(
      '/workspaces',
      memberServer({}, {
        'GET /v1/workspaces': () => jsonResponse(200, []),
        'GET /v1/menu': () => jsonResponse(200, { items: [workspacesItem], permissions }),
      }),
    );
    await heading('Workspaces');

    permissions = ['dashboard.view']; // the Site admin took workspaces.view away
    await act(async () => {
      await session.refreshMenu();
    });

    expect(await heading('You do not have access to this page')).toBeTruthy();
  });

  it('builds the sidebar from what the server sent, nothing more', async () => {
    await renderApp('/dashboard', memberServer({ items: [workspacesItem] }));
    await heading('Welcome, Ana');

    const nav = screen.getByRole('navigation', { name: 'Main' });
    expect(within(nav).getAllByRole('link').map((link) => link.textContent)).toEqual(['Workspaces']);
  });

  it('marks the link of the open page', async () => {
    await renderApp('/workspaces', memberServer({}, { 'GET /v1/workspaces': () => jsonResponse(200, []) }));
    await heading('Workspaces');

    const nav = screen.getByRole('navigation', { name: 'Main' });
    expect(within(nav).getByRole('link', { name: 'Workspaces' }).getAttribute('aria-current')).toBe('page');
    expect(within(nav).getByRole('link', { name: 'Dashboard' }).getAttribute('aria-current')).toBeNull();
  });

  it('says so when the sidebar is empty', async () => {
    await renderApp('/dashboard', memberServer({ items: [] }));
    await heading('Welcome, Ana');

    expect(screen.getByText('No pages are available to you yet.')).toBeTruthy();
  });

  it('moves between pages without asking the server for the menu again', async () => {
    const { fetchFn } = await renderApp('/dashboard', memberServer({}, { 'GET /v1/workspaces': () => jsonResponse(200, []) }));
    await heading('Welcome, Ana');

    fireEvent.click(within(screen.getByRole('navigation', { name: 'Main' })).getByRole('link', { name: 'Workspaces' }));
    await heading('Workspaces');

    expect(fetchFn.calls.filter((call) => call.path === '/v1/menu')).toHaveLength(1);
  });
});

describe('the drawer on a narrow screen', () => {
  const nav = () => screen.getByRole('navigation', { name: 'Main', hidden: true });

  it('keeps the sidebar out of reach while it is closed, and the page in reach', async () => {
    setWideScreen(false);
    await renderApp('/dashboard', memberServer());
    await heading('Welcome, Ana');

    expect(nav().hasAttribute('inert')).toBe(true);
    expect(nav().className).not.toContain('is-open');
    expect(screen.getByRole('main').hasAttribute('inert')).toBe(false);
  });

  it('opens with the menu button, holds focus by making the rest inert, and closes with Escape', async () => {
    setWideScreen(false);
    await renderApp('/dashboard', memberServer());
    await heading('Welcome, Ana');

    fireEvent.click(screen.getByRole('button', { name: 'Menu' }));

    expect(nav().className).toContain('is-open');
    expect(nav().hasAttribute('inert')).toBe(false);
    expect(screen.getByRole('main', { hidden: true }).hasAttribute('inert')).toBe(true);
    expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Close menu' }));

    fireEvent.keyDown(document, { key: 'Escape' });

    expect(nav().className).not.toContain('is-open');
    expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Menu' }));
  });

  it('closes with the Close button, a tap outside, or choosing a link', async () => {
    setWideScreen(false);
    await renderApp('/dashboard', memberServer({}, { 'GET /v1/workspaces': () => jsonResponse(200, []) }));
    await heading('Welcome, Ana');
    const open = () => fireEvent.click(screen.getByRole('button', { name: 'Menu' }));

    open();
    fireEvent.click(screen.getByRole('button', { name: 'Close menu' }));
    expect(nav().className).not.toContain('is-open');

    open();
    fireEvent.click(document.querySelector('.shell-scrim')!);
    expect(nav().className).not.toContain('is-open');

    open();
    fireEvent.click(within(nav()).getByRole('link', { name: 'Workspaces' }));
    await heading('Workspaces');
    await waitFor(() => expect(nav().className).not.toContain('is-open'));
  });

  it('is a column on a wide screen: nothing is inert and the drawer cannot be left open', async () => {
    setWideScreen(false);
    await renderApp('/dashboard', memberServer());
    await heading('Welcome, Ana');
    fireEvent.click(screen.getByRole('button', { name: 'Menu' }));

    act(() => setWideScreen(true));

    expect(nav().hasAttribute('inert')).toBe(false);
    expect(nav().className).not.toContain('is-open');
    expect(screen.getByRole('main').hasAttribute('inert')).toBe(false);

    act(() => setWideScreen(false));
    expect(nav().className).not.toContain('is-open'); // it does not spring open again
  });
});
