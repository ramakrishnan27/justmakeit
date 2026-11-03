import { fireEvent, screen, waitFor } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { PERMISSIONS } from '@/access/permissions';
import { jsonResponse, requestLine } from '@/test/helpers';
import { memberServer, renderApp, type MemberOptions } from '@/test/renderApp';

const heading = (name: string | RegExp) => screen.findByRole('heading', { level: 1, name });
const workspace = (id: string, name: string) => ({ id, name, createdAt: '2026-03-05T10:30:00Z' });
const withCreate = [PERMISSIONS.dashboardView, PERMISSIONS.workspacesView, PERMISSIONS.workspacesCreate, PERMISSIONS.dataRead];

function open(options: MemberOptions, workspaces = [workspace('w1', 'Main')], extra = {}) {
  return renderApp('/workspaces', memberServer(options, { 'GET /v1/workspaces': () => jsonResponse(200, workspaces), ...extra }));
}

describe('the workspaces page', () => {
  it('lists the workspaces with a count and the date they were created', async () => {
    await open({ permissions: withCreate }, [workspace('w1', 'Main'), workspace('w2', 'Spring launch')]);

    expect(await heading('Workspaces')).toBeTruthy();
    expect(await screen.findByText('Spring launch')).toBeTruthy();
    expect(screen.getByText('2 workspaces')).toBeTruthy();
    expect(screen.getAllByText('Created Mar 5, 2026')).toHaveLength(2);
  });

  it('words the page for the kind of organization', async () => {
    await open({ businessModel: 'agency', permissions: withCreate }, [workspace('w1', 'Acme Ltd')]);

    expect(await heading('Client workspaces')).toBeTruthy();
    expect(await screen.findByText('1 client workspace')).toBeTruthy();
    expect(screen.getByRole('heading', { name: 'Add a client workspace' })).toBeTruthy();
  });

  it('shows the add form to a person who holds workspaces.create', async () => {
    await open({ permissions: withCreate });

    expect(await screen.findByRole('heading', { name: 'Add a workspace' })).toBeTruthy();
  });

  it('does not render the add form at all for a person who lacks workspaces.create', async () => {
    await open({ permissions: [PERMISSIONS.dashboardView, PERMISSIONS.workspacesView] });
    await screen.findByText('Main');

    expect(screen.queryByRole('heading', { name: 'Add a workspace' })).toBeNull();
    expect(screen.queryByLabelText(/New workspace name/)).toBeNull();
  });

  it('does not offer a second workspace to an individual creator, even with the permission', async () => {
    await open({ businessModel: 'creator', role: 'contentwriter', permissions: withCreate });
    await screen.findByText('Main');

    expect(screen.queryByRole('heading', { name: /Add a/ })).toBeNull();
  });

  it('asks for a name before it sends anything', async () => {
    const { fetchFn } = await open({ permissions: withCreate });
    await screen.findByRole('heading', { name: 'Add a workspace' });

    fireEvent.click(screen.getByRole('button', { name: 'Add workspace' }));

    expect(screen.getByRole('alert').textContent).toBe('Enter a name for the workspace.');
    expect(document.activeElement).toBe(screen.getByLabelText('New workspace name'));
    expect(fetchFn.calls.map(requestLine)).not.toContain('POST /v1/workspaces');
  });

  it('adds a workspace and reads the list again', async () => {
    let workspaces = [workspace('w1', 'Main')];
    const { fetchFn } = await renderApp(
      '/workspaces',
      memberServer({ permissions: withCreate }, {
        'GET /v1/workspaces': () => jsonResponse(200, workspaces),
        'POST /v1/workspaces': (call) => {
          workspaces = [...workspaces, workspace('w2', JSON.parse(call.init.body as string).name)];
          return jsonResponse(201, workspaces[1]);
        },
      }),
    );
    await screen.findByText('Main');

    fireEvent.change(screen.getByLabelText('New workspace name'), { target: { value: '  Spring launch ' } });
    fireEvent.click(screen.getByRole('button', { name: 'Add workspace' }));

    expect(await screen.findByText('Spring launch')).toBeTruthy();
    expect(screen.getByText('2 workspaces')).toBeTruthy();
    expect(fetchFn.calls.map(requestLine).filter((line) => line === 'GET /v1/workspaces')).toHaveLength(2);
  });

  it('tells the person when the server refuses, and lets them try again', async () => {
    await open({ permissions: withCreate }, [workspace('w1', 'Main')], { 'POST /v1/workspaces': () => jsonResponse(403, { code: 'insufficient_permission' }) });
    await screen.findByRole('heading', { name: 'Add a workspace' });

    fireEvent.change(screen.getByLabelText('New workspace name'), { target: { value: 'Another' } });
    fireEvent.click(screen.getByRole('button', { name: 'Add workspace' }));

    await waitFor(() => expect(screen.getByRole('alert').textContent).toBe('Your role cannot add workspaces.'));
    expect((screen.getByRole('button', { name: 'Add workspace' }) as HTMLButtonElement).disabled).toBe(false);
  });

  it('says so when the list cannot be loaded, and loads it on Try again', async () => {
    let failing = true;
    await renderApp(
      '/workspaces',
      memberServer({}, { 'GET /v1/workspaces': () => (failing ? jsonResponse(500, {}) : jsonResponse(200, [workspace('w1', 'Main')])) }),
    );

    expect((await screen.findByRole('alert')).textContent).toMatch(/could not be loaded/);

    failing = false;
    fireEvent.click(screen.getByRole('button', { name: 'Try again' }));

    expect(await screen.findByText('Main')).toBeTruthy();
    expect(screen.queryByRole('alert')).toBeNull();
  });
});
