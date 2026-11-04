import { fireEvent, screen, waitFor, within } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import type { Team } from '@/api/types';
import { jsonResponse, requestLine, type Handler, type RecordedCall } from '@/test/helpers';
import { memberServer, renderApp, type MemberOptions } from '@/test/renderApp';
import { ana, ben, cara, invitation, manager, member, stale, team } from '@/test/team';

const bodyOf = (call: RecordedCall) => JSON.parse(call.init.body as string);
const rowOf = (text: string) => screen.getByText(text).closest('tr') as HTMLElement;
const button = (name: string | RegExp) => screen.getByRole('button', { name });
const lines = (calls: RecordedCall[]) => calls.map(requestLine);
const lastCall = (calls: RecordedCall[], line: string) => calls.filter((call) => requestLine(call) === line).at(-1);

/**
 * A fake server that holds a team and changes it the way the real one does, so the page's quiet read after each
 * action shows the result. `extra` replaces any route (to make one fail).
 */
function serverWith(initial: Team, extra: Record<string, Handler> = {}, options: MemberOptions = manager) {
  const state = { members: [...initial.members], invitations: [...initial.invitations] };
  const routes: Record<string, Handler> = {
    'GET /v1/members': () => jsonResponse(200, { members: state.members, invitations: state.invitations }),
    'POST /v1/members/invitations': (call) => {
      const { fullName, email, role } = bodyOf(call);
      const created = invitation('i9', email, role, { fullName });
      state.invitations = [...state.invitations, created];
      return jsonResponse(201, { invitation: created, token: 'TOKEN_FROM_THE_SERVER_0123456789_abcdefghij' });
    },
  };
  for (const member of initial.members) {
    routes[`PUT /v1/members/${member.userId}/role`] = (call) => {
      state.members = state.members.map((candidate) => (candidate.userId === member.userId ? { ...candidate, role: bodyOf(call).role } : candidate));
      return new Response(null, { status: 204 });
    };
    routes[`PUT /v1/members/${member.userId}/status`] = (call) => {
      state.members = state.members.map((candidate) => (candidate.userId === member.userId ? { ...candidate, isActive: bodyOf(call).isActive } : candidate));
      return new Response(null, { status: 204 });
    };
    routes[`DELETE /v1/members/${member.userId}`] = () => {
      state.members = state.members.filter((candidate) => candidate.userId !== member.userId);
      return new Response(null, { status: 204 });
    };
  }
  for (const pending of initial.invitations) {
    routes[`DELETE /v1/members/invitations/${pending.id}`] = () => {
      state.invitations = state.invitations.filter((candidate) => candidate.id !== pending.id);
      return new Response(null, { status: 204 });
    };
    routes[`POST /v1/members/invitations/${pending.id}/activate`] = () => {
      state.invitations = state.invitations.filter((candidate) => candidate.id !== pending.id);
      state.members = [...state.members, member(`new-${pending.id}`, pending.fullName || pending.email, pending.role, { email: pending.email, joinedAt: '2026-11-04T10:00:00Z' })];
      return new Response(null, { status: 204 });
    };
  }
  return memberServer(options, { ...routes, ...extra });
}

async function open(initial: Team = team(), extra: Record<string, Handler> = {}, options: MemberOptions = manager) {
  const view = await renderApp('/team', serverWith(initial, extra, options));
  await screen.findByText(initial.members[0]?.fullName ?? 'Ana Smith');
  return view;
}

function fillInvite(email: string, roleTitle?: string, name = 'Test Person') {
  fireEvent.change(screen.getByLabelText('Full name'), { target: { value: name } });
  fireEvent.change(screen.getByLabelText('Email address'), { target: { value: email } });
  if (roleTitle) {
    fireEvent.click(screen.getByRole('radio', { name: new RegExp(`^${roleTitle}`) }));
  }
}

afterEach(() => {
  vi.restoreAllMocks();
  Reflect.deleteProperty(navigator, 'clipboard');
});

describe('New Employee', () => {
  it('opens the invite form in a modal dialog named by its heading, with the focus on the name field', async () => {
    await open();
    expect(screen.queryByRole('dialog')).toBeNull();
    expect(button('New Employee').getAttribute('aria-haspopup')).toBe('dialog');

    fireEvent.click(button('New Employee'));

    const dialog = screen.getByRole('dialog', { name: 'Invite an employee' });
    expect(dialog.getAttribute('aria-modal')).toBe('true');
    expect(within(dialog).getByLabelText('Full name')).toBeTruthy();
    expect(within(dialog).getByLabelText('Email address')).toBeTruthy();
    await waitFor(() => expect(document.activeElement).toBe(screen.getByLabelText('Full name')));
    // The dialog is drawn above the page, not inside it.
    expect(dialog.closest('.team')).toBeNull();
  });

  it('puts the page behind the dialog out of reach while it is open, and gives it back when it closes', async () => {
    await open();
    const page = document.querySelector('.team')!.closest('body > *') as HTMLElement;

    fireEvent.click(button('New Employee'));

    expect(page.hasAttribute('inert')).toBe(true);
    expect(document.documentElement.classList.contains('modal-open')).toBe(true);

    fireEvent.click(button('Cancel'));

    expect(page.hasAttribute('inert')).toBe(false);
    expect(document.documentElement.classList.contains('modal-open')).toBe(false);
    expect(screen.queryByRole('dialog')).toBeNull();
  });

  it('gives focus back to the New Employee button when the dialog closes', async () => {
    await open();
    fireEvent.click(button('New Employee'));

    fireEvent.click(button('Cancel'));

    expect(document.activeElement).toBe(button('New Employee'));
  });

  it.each([
    ['Escape', () => fireEvent.keyDown(document, { key: 'Escape' })],
    ['the × button', () => fireEvent.click(screen.getByRole('button', { name: 'Close' }))],
    ['a tap on the dark area', () => fireEvent.mouseDown(document.querySelector('.modal-backdrop') as HTMLElement)],
  ])('closes the form on %s', async (_how, close) => {
    await open();
    fireEvent.click(button('New Employee'));
    expect(screen.getByRole('dialog')).toBeTruthy();

    close();

    expect(screen.queryByRole('dialog')).toBeNull();
    expect(document.activeElement).toBe(button('New Employee'));
  });

  it('does not close when a press starts inside the dialog, such as selecting text that ends on the dark area', async () => {
    await open();
    fireEvent.click(button('New Employee'));

    fireEvent.mouseDown(screen.getByLabelText('Email address'));

    expect(screen.getByRole('dialog')).toBeTruthy();
  });

  it('keeps Tab inside the dialog, going round from the last control to the first and back', async () => {
    await open();
    fireEvent.click(button('New Employee'));
    const dialog = screen.getByRole('dialog');
    const controls = [...dialog.querySelectorAll<HTMLElement>('button:not([disabled]), input:not([disabled])')];
    const first = controls[0]!;
    const last = controls[controls.length - 1]!;
    expect(first).toBe(screen.getByRole('button', { name: 'Close' }));
    expect(last).toBe(button('Cancel'));

    last.focus();
    fireEvent.keyDown(last, { key: 'Tab' });
    expect(document.activeElement).toBe(first);

    fireEvent.keyDown(first, { key: 'Tab', shiftKey: true });
    expect(document.activeElement).toBe(last);
  });

  it('keeps the link on screen against Escape and a tap outside: it is shown once, so only a button the person chose closes it', async () => {
    await open();
    fireEvent.click(button('New Employee'));
    fillInvite('new@example.com', 'Content Writer');
    fireEvent.click(button('Create invite link'));
    await screen.findByRole('dialog', { name: 'Invite link ready' });

    fireEvent.keyDown(document, { key: 'Escape' });
    fireEvent.mouseDown(document.querySelector('.modal-backdrop') as HTMLElement);

    expect(screen.getByRole('dialog', { name: 'Invite link ready' })).toBeTruthy();
    expect((screen.getByLabelText('Invite link') as HTMLInputElement).value).toMatch(/accept-invite#/);

    fireEvent.click(button('Done'));
    expect(screen.queryByRole('dialog')).toBeNull();
  });

  it('can close the link with the × button, which is a choice', async () => {
    await open();
    fireEvent.click(button('New Employee'));
    fillInvite('new@example.com', 'Content Writer');
    fireEvent.click(button('Create invite link'));
    await screen.findByRole('dialog', { name: 'Invite link ready' });

    fireEvent.click(screen.getByRole('button', { name: 'Close' }));

    expect(screen.queryByRole('dialog')).toBeNull();
    expect(document.body.innerHTML).not.toContain('TOKEN_FROM_THE_SERVER');
  });

  it('cannot be closed on top of a request that is on its way: the invitation would be made and its link lost', async () => {
    let release: () => void = () => undefined;
    const gate = new Promise<void>((resolve) => {
      release = resolve;
    });
    await open(team(), {
      'POST /v1/members/invitations': async () => {
        await gate;
        return jsonResponse(201, { invitation: invitation('i9', 'new@example.com', 'contentwriter'), token: 'TOKEN_FROM_THE_SERVER_0123456789_abcdefghij' });
      },
    });
    fireEvent.click(button('New Employee'));
    fillInvite('new@example.com', 'Content Writer');

    fireEvent.click(button('Create invite link'));
    await waitFor(() => expect((button('Create invite link') as HTMLButtonElement).disabled).toBe(true));

    fireEvent.keyDown(document, { key: 'Escape' });
    fireEvent.mouseDown(document.querySelector('.modal-backdrop') as HTMLElement);
    expect((screen.getByRole('button', { name: 'Close' }) as HTMLButtonElement).disabled).toBe(true);
    expect(screen.getByRole('dialog')).toBeTruthy();

    release();
    expect(await screen.findByRole('dialog', { name: 'Invite link ready' })).toBeTruthy();
  });

  it('offers the roles of a company, with what each is for, and no role selected to start with', async () => {
    await open();
    fireEvent.click(button('New Employee'));

    const roles = screen.getAllByRole('radio').map((radio) => (radio as HTMLInputElement).value);
    expect(roles).toEqual(['organizationadmin', 'marketingmanager', 'contentwriter']);
    expect(screen.getAllByRole('radio').some((radio) => (radio as HTMLInputElement).checked)).toBe(false);
    expect(screen.getByText(/Plans campaigns, manages marketing strategy/)).toBeTruthy();
  });

  it('offers the Client Manager role in a marketing agency only', async () => {
    await open(team(), {}, { ...manager, businessModel: 'agency' });
    fireEvent.click(button('New Employee'));

    expect(screen.getAllByRole('radio').map((radio) => (radio as HTMLInputElement).value)).toEqual([
      'organizationadmin',
      'clientmanager',
      'marketingmanager',
      'contentwriter',
    ]);
  });

  it('asks for an address and a role before it sends anything', async () => {
    const { fetchFn } = await open();
    fireEvent.click(button('New Employee'));

    fireEvent.click(button('Create invite link'));
    expect(screen.getByRole('alert').textContent).toBe("Enter the new employee's name.");
    expect(document.activeElement).toBe(screen.getByLabelText('Full name'));

    fireEvent.change(screen.getByLabelText('Full name'), { target: { value: 'Test Person' } });
    fireEvent.click(button('Create invite link'));
    expect(screen.getByRole('alert').textContent).toBe('Enter your email address.');
    expect(document.activeElement).toBe(screen.getByLabelText('Email address'));

    fillInvite('not-an-address');
    fireEvent.click(button('Create invite link'));
    expect(screen.getByRole('alert').textContent).toBe('Enter a valid email address.');

    fillInvite('new@example.com');
    fireEvent.click(button('Create invite link'));
    expect(screen.getByRole('alert').textContent).toBe('Choose a role for the new employee.');
    expect(lines(fetchFn.calls)).not.toContain('POST /v1/members/invitations');
  });

  it('makes the invitation, shows its link once, and lists the invitation', async () => {
    const { fetchFn } = await open();
    fireEvent.click(button('New Employee'));

    fillInvite('  new@example.com ', 'Content Writer', '  Test Person ');
    fireEvent.click(button('Create invite link'));

    expect(await screen.findByRole('heading', { name: 'Invite link ready' })).toBeTruthy();
    expect(bodyOf(lastCall(fetchFn.calls, 'POST /v1/members/invitations')!)).toEqual({ fullName: 'Test Person', email: 'new@example.com', role: 'contentwriter' });

    const link = (screen.getByLabelText('Invite link') as HTMLInputElement).value;
    expect(link).toBe(`${window.location.origin}/accept-invite#TOKEN_FROM_THE_SERVER_0123456789_abcdefghij`);
    expect(screen.getByText(/Send this link to Test Person \(new@example.com\). It lets them join as Content Writer and works until Jan 15, 2030/)).toBeTruthy();
    expect(screen.getByText(/shown only now/)).toBeTruthy();
    // The table read again by itself, so the new invitation is in it, without the secret.
    expect(await screen.findByText('3 employees, 3 invited')).toBeTruthy();
    expect(within(rowOf('new@example.com')).getByText('Invited')).toBeTruthy();
    expect(rowOf('new@example.com').textContent).not.toContain('TOKEN_FROM_THE_SERVER');
  });

  it('copies the link, and says so', async () => {
    const writeText = vi.fn().mockResolvedValue(undefined);
    Object.defineProperty(navigator, 'clipboard', { value: { writeText }, configurable: true });
    await open();
    fireEvent.click(button('New Employee'));
    fillInvite('new@example.com', 'Marketing Manager');
    fireEvent.click(button('Create invite link'));
    await screen.findByRole('heading', { name: 'Invite link ready' });

    fireEvent.click(button('Copy'));

    await waitFor(() => expect(screen.getByText('Link copied.')).toBeTruthy());
    expect(writeText).toHaveBeenCalledWith(`${window.location.origin}/accept-invite#TOKEN_FROM_THE_SERVER_0123456789_abcdefghij`);
  });

  it('selects the link for the person to copy by hand when the browser has no clipboard', async () => {
    Object.defineProperty(navigator, 'clipboard', { value: undefined, configurable: true });
    await open();
    fireEvent.click(button('New Employee'));
    fillInvite('new@example.com', 'Marketing Manager');
    fireEvent.click(button('Create invite link'));
    await screen.findByRole('heading', { name: 'Invite link ready' });

    fireEvent.click(button('Copy'));

    await waitFor(() => expect(screen.getByText(/Press Ctrl\+C/)).toBeTruthy());
  });

  it('forgets the link when the panel is closed: the secret is nowhere on the page afterwards', async () => {
    await open();
    fireEvent.click(button('New Employee'));
    fillInvite('new@example.com', 'Marketing Manager');
    fireEvent.click(button('Create invite link'));
    await screen.findByRole('heading', { name: 'Invite link ready' });

    fireEvent.click(button('Done'));

    expect(screen.queryByLabelText('Invite link')).toBeNull();
    expect(document.body.innerHTML).not.toContain('TOKEN_FROM_THE_SERVER');
    expect(document.activeElement).toBe(button('New Employee'));
  });

  it('starts another invitation from the link view, with an empty form', async () => {
    await open();
    fireEvent.click(button('New Employee'));
    fillInvite('new@example.com', 'Marketing Manager');
    fireEvent.click(button('Create invite link'));
    await screen.findByRole('heading', { name: 'Invite link ready' });

    fireEvent.click(button('Invite another'));

    expect((screen.getByLabelText('Full name') as HTMLInputElement).value).toBe('');
    expect((screen.getByLabelText('Email address') as HTMLInputElement).value).toBe('');
    expect(screen.queryByLabelText('Invite link')).toBeNull();
  });

  it.each([
    [409, 'email_taken', /already has an account, so it cannot be invited/],
    [409, 'invitation_exists', /already waiting\. Cancel it first/],
    [409, 'team_not_available', /works alone and cannot invite people/],
    [403, 'insufficient_permission', /Your role cannot manage the team/],
    [400, 'validation_failed', /Enter a valid email/],
    [429, 'rate_limited', /Too many requests/],
  ])('tells the person when the server answers %i %s, and lets them try again', async (status, code, message) => {
    await open(team(), {
      'POST /v1/members/invitations': () => jsonResponse(status, { code, errors: status === 400 ? { email: ['Enter a valid email address.'] } : undefined }),
    });
    fireEvent.click(button('New Employee'));
    fillInvite('new@example.com', 'Content Writer');

    fireEvent.click(button('Create invite link'));

    await waitFor(() => expect(screen.getByRole('alert').textContent).toMatch(message));
    expect((button('Create invite link') as HTMLButtonElement).disabled).toBe(false);
    expect(screen.queryByRole('heading', { name: 'Invite link ready' })).toBeNull();
  });
});

describe('changing a role', () => {
  it('sends the new role, says what happens to the person, and shows the role the server now holds', async () => {
    const { fetchFn } = await open();

    fireEvent.change(screen.getByLabelText('Role of Ben Jones'), { target: { value: 'contentwriter' } });

    expect(await screen.findByText('Ben Jones is now Content Writer. They will be asked to sign in again.')).toBeTruthy();
    expect(bodyOf(lastCall(fetchFn.calls, 'PUT /v1/members/u3/role')!)).toEqual({ role: 'contentwriter' });
    await waitFor(() => expect((screen.getByLabelText('Role of Ben Jones') as HTMLSelectElement).value).toBe('contentwriter'));
  });

  it('offers only the roles of this kind of organization', async () => {
    await open();

    const options = within(screen.getByLabelText('Role of Ben Jones')).getAllByRole('option').map((option) => option.textContent);
    expect(options).toEqual(['Organization Admin', 'Marketing Manager', 'Content Writer']);
  });

  it('keeps the old role and says why when the server refuses', async () => {
    await open(team([ana, ben, member('u5', 'Dan Admin', 'organizationadmin')]), {
      'PUT /v1/members/u5/role': () => jsonResponse(409, { code: 'last_organization_admin' }),
    });

    fireEvent.change(screen.getByLabelText('Role of Dan Admin'), { target: { value: 'contentwriter' } });

    expect((await screen.findByRole('alert')).textContent).toBe('An organization needs at least one active organization admin.');
    await waitFor(() => expect((screen.getByLabelText('Role of Dan Admin') as HTMLSelectElement).value).toBe('organizationadmin'));
    expect((screen.getByLabelText('Role of Dan Admin') as HTMLSelectElement).disabled).toBe(false);
  });

  it('refreshes the list when the person is already gone', async () => {
    await open(team(), { 'PUT /v1/members/u3/role': () => jsonResponse(404, { code: 'member_not_found' }) });

    fireEvent.change(screen.getByLabelText('Role of Ben Jones'), { target: { value: 'contentwriter' } });

    expect((await screen.findByRole('alert')).textContent).toMatch(/no longer in the organization/);
  });
});

describe('deleting a person', () => {
  it('asks first, and does nothing when the person says Cancel', async () => {
    const { fetchFn } = await open();

    fireEvent.click(button('Delete Ben Jones'));
    expect(within(rowOf('Ben Jones')).getByText('Delete?')).toBeTruthy();
    fireEvent.click(within(rowOf('Ben Jones')).getByRole('button', { name: 'Cancel' }));

    expect(button('Delete Ben Jones')).toBeTruthy();
    expect(lines(fetchFn.calls).some((line) => line.startsWith('DELETE'))).toBe(false);
  });

  it('deletes the person after the confirmation, and the row goes', async () => {
    const { fetchFn } = await open();

    fireEvent.click(button('Delete Ben Jones'));
    fireEvent.click(button('Yes, delete Ben Jones'));

    expect(await screen.findByText('Ben Jones was deleted.')).toBeTruthy();
    expect(lines(fetchFn.calls)).toContain('DELETE /v1/members/u3');
    await waitFor(() => expect(screen.queryByText('ben@example.com')).toBeNull());
    expect(screen.getByText('2 employees, 2 invited')).toBeTruthy();
  });

  it('keeps the person and says why when the server refuses', async () => {
    await open(team(), { 'DELETE /v1/members/u3': () => jsonResponse(409, { code: 'last_organization_admin' }) });

    fireEvent.click(button('Delete Ben Jones'));
    fireEvent.click(button('Yes, delete Ben Jones'));

    expect((await screen.findByRole('alert')).textContent).toBe('An organization needs at least one active organization admin.');
    expect(screen.getByText('Ben Jones')).toBeTruthy();
    expect(button('Delete Ben Jones')).toBeTruthy();
  });
});

describe('deleting an invitation', () => {
  it('deletes a waiting invitation after the confirmation', async () => {
    const { fetchFn } = await open();

    fireEvent.click(button('Delete dev@example.com'));
    fireEvent.click(button('Yes, delete dev@example.com'));

    expect(await screen.findByText('The invitation for dev@example.com was deleted.')).toBeTruthy();
    expect(lines(fetchFn.calls)).toContain('DELETE /v1/members/invitations/i1');
    await waitFor(() => expect(screen.queryByText('dev@example.com')).toBeNull());
  });

  it('clears out an expired invitation the same way', async () => {
    const { fetchFn } = await open();

    fireEvent.click(button('Delete old@example.com'));
    fireEvent.click(button('Yes, delete old@example.com'));

    await waitFor(() => expect(lines(fetchFn.calls)).toContain('DELETE /v1/members/invitations/i2'));
  });

  it('says so when the invitation was already used or cancelled elsewhere', async () => {
    await open(team(), { 'DELETE /v1/members/invitations/i1': () => jsonResponse(404, { code: 'invitation_not_found' }) });

    fireEvent.click(button('Delete dev@example.com'));
    fireEvent.click(button('Yes, delete dev@example.com'));

    expect((await screen.findByRole('alert')).textContent).toMatch(/not waiting any more/);
  });
});

describe('deactivating and activating a person', () => {
  it('asks before it deactivates, and sends nothing when the person says Cancel', async () => {
    const { fetchFn } = await open();

    fireEvent.click(button('Deactivate Ben Jones'));
    expect(within(rowOf('Ben Jones')).getByText('Deactivate?')).toBeTruthy();
    fireEvent.click(within(rowOf('Ben Jones')).getByRole('button', { name: 'Cancel' }));

    expect(button('Deactivate Ben Jones')).toBeTruthy();
    expect(lines(fetchFn.calls).some((line) => line.startsWith('PUT') && line.endsWith('/status'))).toBe(false);
  });

  it('deactivates the person after the confirmation: the row says Inactive and offers Activate instead', async () => {
    const { fetchFn } = await open();

    fireEvent.click(button('Deactivate Ben Jones'));
    fireEvent.click(button('Yes, deactivate Ben Jones'));

    expect(await screen.findByText(/Ben Jones was deactivated and signed out/)).toBeTruthy();
    expect(bodyOf(lastCall(fetchFn.calls, 'PUT /v1/members/u3/status')!)).toEqual({ isActive: false });
    await waitFor(() => expect(within(rowOf('Ben Jones')).getByText('Inactive')).toBeTruthy());
    expect(button('Activate Ben Jones')).toBeTruthy();
    expect(screen.queryByRole('button', { name: 'Deactivate Ben Jones' })).toBeNull();
    // Still in the list, with the role they have.
    expect((within(rowOf('Ben Jones')).getByLabelText('Role of Ben Jones') as HTMLSelectElement).value).toBe('marketingmanager');
  });

  it('activates a deactivated person in one tap', async () => {
    const { fetchFn } = await open(team([ana, member('u3', 'Ben Jones', 'marketingmanager', { isActive: false }), cara], []));
    expect(within(rowOf('Ben Jones')).getByText('Inactive')).toBeTruthy();

    fireEvent.click(button('Activate Ben Jones'));

    expect(await screen.findByText('Ben Jones is active again and can sign in.')).toBeTruthy();
    expect(bodyOf(lastCall(fetchFn.calls, 'PUT /v1/members/u3/status')!)).toEqual({ isActive: true });
    await waitFor(() => expect(within(rowOf('Ben Jones')).getByText('Active')).toBeTruthy());
    expect(button('Deactivate Ben Jones')).toBeTruthy();
  });

  it('keeps the person active and says why when the server refuses', async () => {
    await open(team(), { 'PUT /v1/members/u3/status': () => jsonResponse(409, { code: 'last_organization_admin' }) });

    fireEvent.click(button('Deactivate Ben Jones'));
    fireEvent.click(button('Yes, deactivate Ben Jones'));

    expect((await screen.findByRole('alert')).textContent).toBe('An organization needs at least one active organization admin.');
    expect(within(rowOf('Ben Jones')).getByText('Active')).toBeTruthy();
    expect(button('Deactivate Ben Jones')).toBeTruthy();
  });

  it('can filter the list down to the people who are inactive', async () => {
    await open(team([ana, member('u3', 'Ben Jones', 'marketingmanager', { isActive: false }), cara], []));

    fireEvent.click(button('Add a filter'));
    fireEvent.click(screen.getByRole('button', { name: 'status is Inactive' }));

    expect(screen.getByText('Ben Jones')).toBeTruthy();
    expect(screen.queryByText('Cara Lee')).toBeNull();
  });

  it('offers Delete on an inactive person too, like on any other', async () => {
    const { fetchFn } = await open(team([ana, member('u3', 'Ben Jones', 'marketingmanager', { isActive: false })], []));

    fireEvent.click(button('Delete Ben Jones'));
    fireEvent.click(button('Yes, delete Ben Jones'));

    await waitFor(() => expect(lines(fetchFn.calls)).toContain('DELETE /v1/members/u3'));
  });
});

describe('activating an invited person by hand', () => {
  const named = invitation('i3', 'dev@example.com', 'contentwriter', { fullName: 'Dev Kumar' });

  it('names the invited person in the row, and offers Activate and Delete', async () => {
    await open(team([ana], [named]));

    const row = within(rowOf('Dev Kumar'));
    expect(row.getByText('dev@example.com')).toBeTruthy();
    expect(row.getByText('Invited')).toBeTruthy();
    expect(button('Activate Dev Kumar')).toBeTruthy();
    expect(button('Delete Dev Kumar')).toBeTruthy();
  });

  it('opens a dialog that asks for the default password, and says what will happen', async () => {
    await open(team([ana], [named]));

    fireEvent.click(button('Activate Dev Kumar'));

    const dialog = screen.getByRole('dialog', { name: 'Activate Dev Kumar' });
    expect(within(dialog).getByText(/sign in as dev@example.com with it straight away, and the invitation link stops working/)).toBeTruthy();
    expect(within(dialog).getByText(/You will know this password.*There is no screen yet where they can change it/)).toBeTruthy();
    await waitFor(() => expect(document.activeElement).toBe(within(dialog).getByLabelText('Default password')));
    expect((within(dialog).getByLabelText('Default password') as HTMLInputElement).type).toBe('password');
  });

  it('asks for at least 10 characters before it sends anything', async () => {
    const { fetchFn } = await open(team([ana], [named]));
    fireEvent.click(button('Activate Dev Kumar'));

    fireEvent.change(screen.getByLabelText('Default password'), { target: { value: 'short' } });
    fireEvent.click(button('Activate'));

    expect(screen.getByRole('alert').textContent).toBe('Choose a password of at least 10 characters.');
    expect(fetchFn.calls.map(requestLine).some((line) => line.endsWith('/activate'))).toBe(false);
  });

  it('activates the person: the dialog closes, the person is active in the list, and the password is gone from the page', async () => {
    const { fetchFn } = await open(team([ana], [named]));
    fireEvent.click(button('Activate Dev Kumar'));

    fireEvent.change(screen.getByLabelText('Default password'), { target: { value: 'the default password 1' } });
    fireEvent.click(button('Activate'));

    expect(await screen.findByText('Dev Kumar is active now. They can sign in as dev@example.com with the password you set.')).toBeTruthy();
    expect(bodyOf(lastCall(fetchFn.calls, 'POST /v1/members/invitations/i3/activate')!)).toEqual({ password: 'the default password 1' });
    expect(screen.queryByRole('dialog')).toBeNull();
    await waitFor(() => expect(within(rowOf('Dev Kumar')).getByText('Active')).toBeTruthy());
    expect(button('Deactivate Dev Kumar')).toBeTruthy();
    expect(screen.queryByRole('button', { name: 'Activate Dev Kumar' })).toBeNull();
    expect(document.body.innerHTML).not.toContain('the default password 1');
  });

  it('closes without sending anything on Cancel, and gives focus back to the list', async () => {
    const { fetchFn } = await open(team([ana], [named]));
    fireEvent.click(button('Activate Dev Kumar'));

    fireEvent.click(button('Cancel'));

    expect(screen.queryByRole('dialog')).toBeNull();
    expect(fetchFn.calls.map(requestLine).some((line) => line.endsWith('/activate'))).toBe(false);
    expect(document.activeElement).toBe(document.querySelector('.table-scroll'));
  });

  it('can also activate an invitation whose link has expired', async () => {
    const { fetchFn } = await open(team([ana], [stale]));

    fireEvent.click(button('Activate old@example.com'));
    fireEvent.change(screen.getByLabelText('Default password'), { target: { value: 'the default password 1' } });
    fireEvent.click(button('Activate'));

    await waitFor(() => expect(lines(fetchFn.calls)).toContain('POST /v1/members/invitations/i2/activate'));
  });

  it.each([
    [409, 'email_taken', /now has an account.*Delete the invitation/],
    [404, 'invitation_not_found', /not waiting any more/],
    [400, 'validation_failed', /Password must be at least 10 characters/],
    [403, 'insufficient_permission', /cannot manage the team/],
  ])('tells the person when the server answers %i %s, and keeps the dialog open to try again', async (status, code, message) => {
    await open(team([ana], [named]), {
      'POST /v1/members/invitations/i3/activate': () =>
        jsonResponse(status, { code, errors: status === 400 ? { password: ['Password must be at least 10 characters.'] } : undefined }),
    });
    fireEvent.click(button('Activate Dev Kumar'));
    fireEvent.change(screen.getByLabelText('Default password'), { target: { value: 'the default password 1' } });

    fireEvent.click(button('Activate'));

    await waitFor(() => expect(screen.getByRole('alert').textContent).toMatch(message));
    expect(screen.getByRole('dialog', { name: 'Activate Dev Kumar' })).toBeTruthy();
    expect((button('Activate') as HTMLButtonElement).disabled).toBe(false);
  });

  it('cannot be closed on top of a request that is on its way', async () => {
    let release: () => void = () => undefined;
    const gate = new Promise<void>((resolve) => {
      release = resolve;
    });
    await open(team([ana], [named]), {
      'POST /v1/members/invitations/i3/activate': async () => {
        await gate;
        return new Response(null, { status: 204 });
      },
    });
    fireEvent.click(button('Activate Dev Kumar'));
    fireEvent.change(screen.getByLabelText('Default password'), { target: { value: 'the default password 1' } });

    fireEvent.click(button('Activate'));
    await waitFor(() => expect((button('Activate') as HTMLButtonElement).disabled).toBe(true));

    fireEvent.keyDown(document, { key: 'Escape' });
    fireEvent.mouseDown(document.querySelector('.modal-backdrop') as HTMLElement);
    expect((screen.getByRole('button', { name: 'Close' }) as HTMLButtonElement).disabled).toBe(true);
    expect(screen.getByRole('dialog')).toBeTruthy();

    release();
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
  });
});

describe('selecting rows', () => {
  it('ticks every row but your own with the box in the header, and unticks them again', async () => {
    await open();
    const all = screen.getByRole('checkbox', { name: 'Select all' }) as HTMLInputElement;

    fireEvent.click(all);

    expect(screen.getByText('4 selected')).toBeTruthy();
    expect((screen.getByRole('checkbox', { name: 'Select Ben Jones' }) as HTMLInputElement).checked).toBe(true);
    expect((screen.getByRole('checkbox', { name: 'Select Ana Smith' }) as HTMLInputElement).checked).toBe(false);
    expect(all.checked).toBe(true);

    fireEvent.click(all);
    expect(screen.queryByText(/selected$/)).toBeNull();
  });

  it('shows the header box as partly ticked when only some rows are', async () => {
    await open();

    fireEvent.click(screen.getByRole('checkbox', { name: 'Select Ben Jones' }));

    const all = screen.getByRole('checkbox', { name: 'Select all' }) as HTMLInputElement;
    expect(all.indeterminate).toBe(true);
    expect(all.checked).toBe(false);
    expect(screen.getByText('1 selected')).toBeTruthy();
  });

  it('deletes the ticked rows after one confirmation, people and invitations alike', async () => {
    const { fetchFn } = await open();
    fireEvent.click(screen.getByRole('checkbox', { name: 'Select Ben Jones' }));
    fireEvent.click(screen.getByRole('checkbox', { name: 'Select dev@example.com' }));

    fireEvent.click(button('Delete selected'));
    expect(screen.getByText('Delete 1 person and 1 invitation?')).toBeTruthy();
    expect(lines(fetchFn.calls).some((line) => line.startsWith('DELETE'))).toBe(false);
    fireEvent.click(button('Yes, continue'));

    expect(await screen.findByText('Deleted 1 person and 1 invitation.')).toBeTruthy();
    expect(lines(fetchFn.calls).filter((line) => line.startsWith('DELETE'))).toEqual(['DELETE /v1/members/u3', 'DELETE /v1/members/invitations/i1']);
    await waitFor(() => expect(screen.queryByText('Ben Jones')).toBeNull());
    expect(screen.queryByText(/selected$/)).toBeNull();
  });

  it('says how far it got when the server refuses one of them', async () => {
    await open(team(), { 'DELETE /v1/members/u3': () => jsonResponse(409, { code: 'last_organization_admin' }) });
    fireEvent.click(screen.getByRole('checkbox', { name: 'Select Ben Jones' }));
    fireEvent.click(screen.getByRole('checkbox', { name: 'Select Cara Lee' }));

    fireEvent.click(button('Delete selected'));
    fireEvent.click(button('Yes, continue'));

    expect((await screen.findByRole('alert')).textContent).toBe('1 of 2 done. Ben Jones: An organization needs at least one active organization admin.');
    expect(screen.getByText('Ben Jones')).toBeTruthy();
    await waitFor(() => expect(screen.queryByText('Cara Lee')).toBeNull());
  });

  it('stops at the first 401: the rest would only be refused the same way', async () => {
    const { fetchFn } = await open(team(), { 'DELETE /v1/members/u3': () => jsonResponse(401, { code: 'session_ended' }) });
    fireEvent.click(screen.getByRole('checkbox', { name: 'Select all' }));

    fireEvent.click(button('Delete selected'));
    fireEvent.click(button('Yes, continue'));

    await waitFor(() => expect(lines(fetchFn.calls)).toContain('DELETE /v1/members/u3'));
    expect(lines(fetchFn.calls)).not.toContain('DELETE /v1/members/u4');
  });

  it('never deletes a row the search has hidden, however it was ticked before', async () => {
    const { fetchFn } = await open();
    fireEvent.click(screen.getByRole('checkbox', { name: 'Select Ben Jones' }));
    fireEvent.click(screen.getByRole('checkbox', { name: 'Select Cara Lee' }));

    fireEvent.change(screen.getByRole('searchbox'), { target: { value: 'cara' } });
    expect(screen.getByText('1 selected')).toBeTruthy();
    fireEvent.click(button('Delete selected'));
    fireEvent.click(button('Yes, continue'));

    await waitFor(() => expect(lines(fetchFn.calls)).toContain('DELETE /v1/members/u4'));
    expect(lines(fetchFn.calls)).not.toContain('DELETE /v1/members/u3');
  });

  it('clears the selection on request', async () => {
    await open();
    fireEvent.click(screen.getByRole('checkbox', { name: 'Select Ben Jones' }));

    fireEvent.click(button('Clear selection'));

    expect(screen.queryByText('1 selected')).toBeNull();
    expect((screen.getByRole('checkbox', { name: 'Select Ben Jones' }) as HTMLInputElement).checked).toBe(false);
  });
});
