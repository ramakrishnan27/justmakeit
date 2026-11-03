import { act, render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { createApiClient } from '@/api/client';
import { createSession } from '@/session/session';
import { SessionProvider } from '@/session/SessionContext';
import { jsonResponse, routedFetch } from '@/test/helpers';
import { memberServer } from '@/test/renderApp';
import { Can } from './Can';
import { PERMISSIONS } from './permissions';

async function signedInWith(permissions: string[]) {
  const api = createApiClient({ fetchFn: routedFetch(memberServer({ permissions })) });
  const session = createSession(api);
  await session.restore();
  return { api, session };
}

function renderCan(services: Awaited<ReturnType<typeof signedInWith>>, ui: React.ReactNode) {
  return render(<SessionProvider api={services.api} session={services.session}>{ui}</SessionProvider>);
}

describe('<Can>', () => {
  it('renders its children for a person who holds the permission', async () => {
    renderCan(await signedInWith([PERMISSIONS.workspacesCreate]), <Can permission={PERMISSIONS.workspacesCreate}>the form</Can>);

    expect(screen.getByText('the form')).toBeTruthy();
  });

  it('renders the fallback, and not the children, for a person who lacks it', async () => {
    renderCan(
      await signedInWith([PERMISSIONS.dashboardView]),
      <Can permission={PERMISSIONS.workspacesCreate} fallback={<p>read only</p>}>
        the form
      </Can>,
    );

    expect(screen.queryByText('the form')).toBeNull();
    expect(screen.getByText('read only')).toBeTruthy();
  });

  it('renders nothing for a person who lacks it when there is no fallback', async () => {
    const { container } = renderCan(await signedInWith([]), <Can permission={PERMISSIONS.dataRead}>the form</Can>);

    expect(container.textContent).toBe('');
  });

  it('does not even create the children it is not allowed to show', async () => {
    let created = 0;
    function Child() {
      created += 1;
      return <p>child</p>;
    }
    renderCan(await signedInWith([]), <Can permission={PERMISSIONS.dataRead}><Child /></Can>);

    expect(created).toBe(0);
  });

  it('supports anyOf and allOf', async () => {
    renderCan(
      await signedInWith([PERMISSIONS.dataRead]),
      <>
        <Can anyOf={[PERMISSIONS.dataWrite, PERMISSIONS.dataRead]}><p>any: yes</p></Can>
        <Can anyOf={[PERMISSIONS.dataWrite]} fallback={<p>any: no</p>}><p>hidden</p></Can>
        <Can allOf={[PERMISSIONS.dataRead]}><p>all: yes</p></Can>
        <Can allOf={[PERMISSIONS.dataRead, PERMISSIONS.dataWrite]} fallback={<p>all: no</p>}><p>hidden</p></Can>
      </>,
    );

    expect(screen.getByText('any: yes')).toBeTruthy();
    expect(screen.getByText('any: no')).toBeTruthy();
    expect(screen.getByText('all: yes')).toBeTruthy();
    expect(screen.getByText('all: no')).toBeTruthy();
    expect(screen.queryByText('hidden')).toBeNull();
  });

  it('shows nothing when it is not told what to check, rather than everything', async () => {
    renderCan(await signedInWith(Object.values(PERMISSIONS)), <Can fallback="denied">secret</Can>);

    expect(screen.queryByText('secret')).toBeNull();
    expect(screen.getByText('denied')).toBeTruthy();
  });

  it('shows nothing to a visitor who is not signed in', async () => {
    const api = createApiClient({ fetchFn: routedFetch({ 'GET /v1/auth/me': () => jsonResponse(401, {}) }) });
    const session = createSession(api);
    await session.restore();

    renderCan({ api, session }, <Can permission={PERMISSIONS.dashboardView}>secret</Can>);

    expect(screen.queryByText('secret')).toBeNull();
  });

  it('changes at once when the permissions in the session change', async () => {
    let permissions: string[] = [PERMISSIONS.dataRead];
    const api = createApiClient({ fetchFn: routedFetch(memberServer({}, { 'GET /v1/menu': () => jsonResponse(200, { items: [], permissions }) })) });
    const session = createSession(api);
    await session.restore();
    renderCan({ api, session }, <Can permission={PERMISSIONS.dataRead} fallback="gone">the data</Can>);
    expect(screen.getByText('the data')).toBeTruthy();

    permissions = [];
    await act(async () => {
      await session.refreshMenu();
    });

    expect(screen.queryByText('the data')).toBeNull();
    expect(screen.getByText('gone')).toBeTruthy();
  });
});
