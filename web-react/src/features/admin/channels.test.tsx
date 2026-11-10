import { act, fireEvent, screen, waitFor, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { formatDate } from '@/lib/format';
import { adminApp } from '@/navigation/apps';
import { jsonResponse, requestLine } from '@/test/helpers';
import { adminDashboardItem, adminServer, builtInChannels, renderApp, siteChannel } from '@/test/renderApp';

const channelsItem = { id: 'a5', pageKey: 'channels', label: 'Channels', icon: 'list', path: '/admin/channels' };

const heading = (name: string | RegExp) => screen.findByRole('heading', { level: 1, name });
const open = (overrides: Parameters<typeof adminServer>[1] = {}) =>
  renderApp('/admin/channels', adminServer({ items: [adminDashboardItem, channelsItem] }, overrides), adminApp);
const tileOf = (name: string) => screen.getByText(name, { selector: '.channel-name' }).closest('li') as HTMLElement;
const names = () => [...document.querySelectorAll('.channel-name')].map((node) => node.textContent);
const png = (size = 2_000, name = 'icon.png', type = 'image/png') => new File([new Uint8Array(size)], name, { type });
const choose = (file: File) => fireEvent.change(screen.getByLabelText(/^Icon/), { target: { files: [file] } });
const type = (label: string | RegExp, value: string) => fireEvent.change(screen.getByLabelText(label), { target: { value } });

async function openDialog() {
  fireEvent.click(await screen.findByRole('button', { name: 'New channel' }));
  return await screen.findByRole('dialog', { name: 'New channel' });
}

const createdTelegram = (overrides: Record<string, unknown> = {}) =>
  siteChannel('Telegram', { id: 'ch-new', description: 'Fast chat', createdByName: 'Root', createdAt: '2026-11-05T10:00:00Z', ...overrides });

describe('the Channels page', () => {
  it('lists every channel the server sends, as icons, in the server order, each with its name', async () => {
    await open();
    await heading('Channels');

    const list = await screen.findByRole('list', { name: 'Channels' });
    expect(within(list).getAllByRole('listitem')).toHaveLength(7);
    expect(names()).toEqual(['Instagram', 'Facebook', 'YouTube', 'Email', 'SMS', 'WhatsApp', 'LinkedIn']);
    for (const tile of within(list).getAllByRole('listitem')) {
      const icon = tile.querySelector('svg');
      expect(icon, tile.textContent ?? '').not.toBeNull();
      expect(icon?.getAttribute('aria-hidden')).toBe('true');
    }
    expect(document.title).toBe('Channels · Site admin');
  });

  it('shows what each is for, how many organizations have it, and that the built-in ones are built in', async () => {
    await open();

    await screen.findByRole('list', { name: 'Channels' });
    const email = within(tileOf('Email'));
    expect(email.getByText('Send email campaigns to contacts.')).toBeTruthy();
    expect(email.getByText('Used by 2 organizations')).toBeTruthy();
    expect(email.getByText('Built in')).toBeTruthy();
    expect(within(tileOf('Instagram')).getByText('Used by 0 organizations')).toBeTruthy();
  });

  it('says "1 organization" for one, and shows who added a channel and when, with its uploaded picture', async () => {
    const mine = siteChannel('Telegram', {
      id: 'ch-1',
      iconKey: null,
      hasIconImage: true,
      createdByName: 'Rama Admin',
      createdAt: '2026-11-05T10:00:00Z',
      organizationCount: 1,
    });
    await open({ 'GET /v1/site/channels': () => jsonResponse(200, [...builtInChannels, mine]) });

    await screen.findByRole('list', { name: 'Channels' });
    const tile = within(tileOf('Telegram'));
    expect(tile.getByText('Used by 1 organization')).toBeTruthy();
    expect(tile.getByText(`Added by Rama Admin · ${formatDate('2026-11-05T10:00:00Z')}`)).toBeTruthy();
    const image = tileOf('Telegram').querySelector('img');
    expect(image?.getAttribute('src')).toBe('/v1/site/channels/ch-1/icon');
    expect(image?.getAttribute('alt')).toBe('');
    expect(tileOf('Telegram').querySelector('svg')).toBeNull();
    expect(tileOf('Email').querySelector('img')).toBeNull();
  });

  it('reads nothing but the channels beyond the session', async () => {
    const { fetchFn } = await open();
    await screen.findByRole('list', { name: 'Channels' });

    expect(fetchFn.calls.map(requestLine).filter((line) => !['GET /v1/site/me', 'GET /v1/site/menu'].includes(line))).toEqual(['GET /v1/site/channels']);
  });

  it('says what went wrong, and loads again on Try again', async () => {
    let failing = true;
    await open({ 'GET /v1/site/channels': () => (failing ? jsonResponse(500, {}) : jsonResponse(200, builtInChannels)) });

    expect((await screen.findByRole('alert')).textContent).toMatch(/The channels could not be loaded\./);

    failing = false;
    fireEvent.click(screen.getByRole('button', { name: 'Try again' }));

    expect(await screen.findByRole('list', { name: 'Channels' })).toBeTruthy();
    expect(screen.queryByRole('alert')).toBeNull();
  });

  it('tells a person who is no longer a Site admin so', async () => {
    await open({ 'GET /v1/site/channels': () => jsonResponse(403, { code: 'not_site_admin' }) });

    expect((await screen.findByRole('alert')).textContent).toMatch(/no longer a site admin/);
  });

  it('invites the first channel when there are none', async () => {
    await open({ 'GET /v1/site/channels': () => jsonResponse(200, []) });

    expect(await screen.findByText(/No channels yet/)).toBeTruthy();
    expect(screen.queryByRole('list', { name: 'Channels' })).toBeNull();
    expect(screen.getByRole('button', { name: 'New channel' })).toBeTruthy();
  });
});

describe('the New channel dialog', () => {
  it('opens on the name, named by its heading, and closes with Cancel, giving focus back to the button', async () => {
    await open();
    const dialog = await openDialog();

    expect(document.activeElement).toBe(within(dialog).getByLabelText('Name'));
    expect(within(dialog).getByLabelText('Description (optional)')).toBeTruthy();
    expect(within(dialog).getByLabelText('Icon (optional)')).toBeTruthy();

    fireEvent.click(within(dialog).getByRole('button', { name: 'Cancel' }));

    expect(screen.queryByRole('dialog')).toBeNull();
    expect(document.activeElement).toBe(screen.getByRole('button', { name: 'New channel' }));
  });

  it('closes on Escape and on the ×, and what was typed is not kept for next time', async () => {
    await open();
    let dialog = await openDialog();
    type('Name', 'Half typed');

    fireEvent.keyDown(document, { key: 'Escape' });
    expect(screen.queryByRole('dialog')).toBeNull();

    dialog = await openDialog();
    expect((within(dialog).getByLabelText('Name') as HTMLInputElement).value).toBe('');
    fireEvent.click(within(dialog).getByRole('button', { name: 'Close' }));
    expect(screen.queryByRole('dialog')).toBeNull();
  });

  it('asks for a name before it sends anything, and puts the cursor there', async () => {
    const { fetchFn } = await open();
    await openDialog();

    fireEvent.click(screen.getByRole('button', { name: 'Add channel' }));

    expect(screen.getByRole('alert').textContent).toBe('Enter a name for the channel.');
    expect(document.activeElement).toBe(screen.getByLabelText('Name'));
    expect(screen.getByLabelText('Name').getAttribute('aria-invalid')).toBe('true');
    expect(fetchFn.calls.map(requestLine)).not.toContain('POST /v1/site/channels');
  });

  it('refuses a description that is too long before it sends anything', async () => {
    const { fetchFn } = await open();
    await openDialog();
    type('Name', 'Telegram');
    // The field itself stops at 300 characters; a pasted longer text is checked too.
    fireEvent.change(screen.getByLabelText('Description (optional)'), { target: { value: 'x'.repeat(301) } });

    fireEvent.click(screen.getByRole('button', { name: 'Add channel' }));

    expect(screen.getByRole('alert').textContent).toBe('The description must be at most 300 characters.');
    expect(fetchFn.calls.map(requestLine)).not.toContain('POST /v1/site/channels');
  });

  it.each([
    ['an SVG', png(100, 'logo.svg', 'image/svg+xml'), 'The icon must be a PNG, JPEG or WebP image.'],
    ['a GIF', png(100, 'logo.gif', 'image/gif'), 'The icon must be a PNG, JPEG or WebP image.'],
    ['an empty file', png(0), 'The icon file is empty.'],
    ['a file over 100 KB', png(100 * 1024 + 1), 'The icon must be at most 100 KB.'],
  ])('refuses %s the moment it is chosen, and keeps no file: what is sent afterwards has no icon', async (_what, file, message) => {
    let received: FormData | null = null;
    await open({
      'POST /v1/site/channels': (call) => {
        received = call.init.body as FormData;
        return jsonResponse(201, createdTelegram());
      },
    });
    await openDialog();

    choose(file);

    expect(screen.getByRole('alert').textContent).toBe(message);
    expect(screen.queryByText('Remove')).toBeNull();
    expect(screen.queryByAltText('Preview of the chosen icon')).toBeNull();

    type('Name', 'Telegram');
    fireEvent.click(screen.getByRole('button', { name: 'Add channel' }));

    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
    expect((received as unknown as FormData).has('icon')).toBe(false);
  });

  it('accepts an icon of exactly 100 KB, shows what was chosen, and takes it back on Remove', async () => {
    let received: FormData | null = null;
    await open({
      'POST /v1/site/channels': (call) => {
        received = call.init.body as FormData;
        return jsonResponse(201, createdTelegram());
      },
    });
    await openDialog();

    choose(png(100 * 1024, 'big.png'));

    expect(await screen.findByAltText('Preview of the chosen icon')).toBeTruthy();
    expect(screen.getByText('big.png')).toBeTruthy();
    expect(screen.queryByRole('alert')).toBeNull();
    expect(screen.getByAltText('Preview of the chosen icon').getAttribute('src')).toMatch(/^data:image\/png;base64,/);

    fireEvent.click(screen.getByRole('button', { name: 'Remove' }));

    expect(screen.queryByAltText('Preview of the chosen icon')).toBeNull();
    expect(screen.queryByText('big.png')).toBeNull();

    type('Name', 'Telegram');
    fireEvent.click(screen.getByRole('button', { name: 'Add channel' }));
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
    expect((received as unknown as FormData).has('icon')).toBe(false);
  });

  it('shows the preview of the file chosen last, not of an earlier one', async () => {
    await open();
    await openDialog();

    choose(png(1_000, 'first.png'));
    choose(png(2_000, 'second.png'));

    await waitFor(() => expect(screen.getByText('second.png')).toBeTruthy());
    expect(screen.queryByText('first.png')).toBeNull();
    expect(await screen.findByAltText('Preview of the chosen icon')).toBeTruthy();
  });

  it('sends the name, the description and the icon as a form, then closes, lists the new channel last and says so', async () => {
    let received: FormData | null = null;
    let contentType: string | undefined;
    const { fetchFn } = await open({
      'POST /v1/site/channels': (call) => {
        received = call.init.body as FormData;
        contentType = call.headers['Content-Type'];
        return jsonResponse(201, createdTelegram({ iconKey: null, hasIconImage: true }));
      },
    });
    await openDialog();
    type('Name', '  Telegram  ');
    type('Description (optional)', ' Fast chat ');
    choose(png(2_000, 'telegram.png'));
    await screen.findByAltText('Preview of the chosen icon');

    fireEvent.click(screen.getByRole('button', { name: 'Add channel' }));

    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
    expect(fetchFn.calls.map(requestLine)).toContain('POST /v1/site/channels');
    expect(received).toBeInstanceOf(FormData);
    expect(contentType).toBeUndefined(); // the browser writes it, with the boundary
    const form = received as unknown as FormData;
    expect(form.get('name')).toBe('Telegram');
    expect(form.get('description')).toBe('Fast chat');
    const icon = form.get('icon');
    expect(icon).toBeInstanceOf(File);
    expect((icon as File).name).toBe('telegram.png');
    expect((icon as File).type).toBe('image/png');
    expect((icon as File).size).toBe(2_000);

    expect(names().at(-1)).toBe('Telegram');
    expect(names()).toHaveLength(8);
    const tile = within(tileOf('Telegram'));
    expect(tile.getByText('Added by Root · ' + formatDate('2026-11-05T10:00:00Z'))).toBeTruthy();
    expect(tileOf('Telegram').querySelector('img')?.getAttribute('src')).toBe('/v1/site/channels/ch-new/icon');
    const feedback = screen.getByText(/^Telegram was added\./);
    expect(feedback.getAttribute('role')).toBe('status');
    expect(feedback.textContent).toBe('Telegram was added. Turn it on for organizations from Users, Organizations.');
    expect(document.activeElement).toBe(screen.getByRole('button', { name: 'New channel' }));
  });

  it('sends no icon part when none was chosen, and the channel is drawn with its built-in icon', async () => {
    let received: FormData | null = null;
    await open({
      'POST /v1/site/channels': (call) => {
        received = call.init.body as FormData;
        return jsonResponse(201, createdTelegram());
      },
    });
    await openDialog();
    type('Name', 'Telegram');

    fireEvent.click(screen.getByRole('button', { name: 'Add channel' }));

    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
    const form = received as unknown as FormData;
    expect([...form.keys()]).toEqual(['name', 'description']);
    expect(form.get('description')).toBe('');
    expect(tileOf('Telegram').querySelector('svg')).not.toBeNull();
    expect(tileOf('Telegram').querySelector('img')).toBeNull();
  });

  it('keeps the dialog open and says so under the name when the name is taken, with what was typed still there', async () => {
    await open({ 'POST /v1/site/channels': () => jsonResponse(409, { code: 'channel_exists', detail: 'A channel with that name already exists.' }) });
    await openDialog();
    type('Name', 'email');
    type('Description (optional)', 'Mine');
    choose(png(2_000, 'mine.png'));
    await screen.findByAltText('Preview of the chosen icon');

    fireEvent.click(screen.getByRole('button', { name: 'Add channel' }));

    expect((await screen.findByRole('alert')).textContent).toBe('A channel with that name already exists.');
    expect(screen.getByRole('dialog', { name: 'New channel' })).toBeTruthy();
    expect(document.activeElement).toBe(screen.getByLabelText('Name'));
    expect((screen.getByLabelText('Name') as HTMLInputElement).value).toBe('email');
    expect((screen.getByLabelText('Description (optional)') as HTMLInputElement).value).toBe('Mine');
    expect(screen.getByText('mine.png')).toBeTruthy();
    expect((screen.getByRole('button', { name: 'Add channel' }) as HTMLButtonElement).disabled).toBe(false);
    expect(names()).toHaveLength(7);
  });

  it('takes a message back once the person edits the field it is about, and leaves the others alone', async () => {
    await open({ 'POST /v1/site/channels': () => jsonResponse(409, { code: 'channel_exists' }) });
    await openDialog();
    type('Name', 'email');
    fireEvent.click(screen.getByRole('button', { name: 'Add channel' }));
    await screen.findByText('A channel with that name already exists.');
    choose(png(100, 'x.svg', 'image/svg+xml')); // refused at once: a second message, under the icon
    expect((await screen.findAllByRole('alert')).map((node) => node.textContent)).toEqual([
      'A channel with that name already exists.',
      'The icon must be a PNG, JPEG or WebP image.',
    ]);

    type('Name', 'Signal');

    expect(screen.getAllByRole('alert').map((node) => node.textContent)).toEqual(['The icon must be a PNG, JPEG or WebP image.']);
    expect(screen.getByLabelText('Name').getAttribute('aria-invalid')).toBeNull();
  });

  it('puts what the server says about the icon under the icon', async () => {
    await open({
      'POST /v1/site/channels': () => jsonResponse(400, { errors: { icon: ['The icon must be a PNG, JPEG or WebP image.'] } }),
    });
    await openDialog();
    type('Name', 'Telegram');
    choose(png());

    fireEvent.click(screen.getByRole('button', { name: 'Add channel' }));

    expect((await screen.findByRole('alert')).textContent).toBe('The icon must be a PNG, JPEG or WebP image.');
    expect(document.activeElement).toBe(screen.getByLabelText(/^Icon/));
  });

  it('says so when the server fails or cannot be reached, and lets the person try again', async () => {
    let failing = true;
    await open({
      'POST /v1/site/channels': () => (failing ? jsonResponse(500, {}) : jsonResponse(201, createdTelegram())),
    });
    await openDialog();
    type('Name', 'Telegram');

    fireEvent.click(screen.getByRole('button', { name: 'Add channel' }));
    expect((await screen.findByRole('alert')).textContent).toBe('The channel could not be added. Try again in a moment.');

    failing = false;
    fireEvent.click(screen.getByRole('button', { name: 'Add channel' }));

    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
    expect(names()).toContain('Telegram');
  });

  it('cannot be closed or sent twice while the request is on its way', async () => {
    let release: (response: Response) => void = () => undefined;
    const { fetchFn } = await open({
      'POST /v1/site/channels': () => new Promise<Response>((resolve) => (release = resolve)),
    });
    await openDialog();
    type('Name', 'Telegram');

    fireEvent.click(screen.getByRole('button', { name: 'Add channel' }));

    await waitFor(() => expect((screen.getByRole('button', { name: 'Add channel' }) as HTMLButtonElement).disabled).toBe(true));
    expect((screen.getByRole('button', { name: 'Cancel' }) as HTMLButtonElement).disabled).toBe(true);
    expect((screen.getByRole('button', { name: 'Close' }) as HTMLButtonElement).disabled).toBe(true);
    fireEvent.keyDown(document, { key: 'Escape' });
    expect(screen.getByRole('dialog')).toBeTruthy();
    expect(fetchFn.calls.map(requestLine).filter((line) => line === 'POST /v1/site/channels')).toHaveLength(1);

    await act(async () => {
      release(jsonResponse(201, createdTelegram()));
    });

    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
    expect(names()).toContain('Telegram');
  });
});
