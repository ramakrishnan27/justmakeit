import { fireEvent, screen, waitFor, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { PERMISSIONS } from '@/access/permissions';
import { jsonResponse, requestLine } from '@/test/helpers';
import { allContentPermissions, contentServer, post, target, type ContentServerOptions } from '@/test/content';
import { renderApp } from '@/test/renderApp';

const heading = (name: string | RegExp) => screen.findByRole('heading', { level: 1, name });
const location = () => screen.getByTestId('location').textContent;

const writer = [PERMISSIONS.dashboardView, PERMISSIONS.workspacesView, PERMISSIONS.contentStudioView, PERMISSIONS.contentStudioCreate];
const reader = [PERMISSIONS.dashboardView, PERMISSIONS.contentStudioView];

const sample = () => [
  post('p1', { title: 'Morning motivation', status: 'published', publishedAt: '2026-11-06T09:00:00.0000000+00:00', createdAt: '2026-11-06T08:00:00.0000000+00:00', platforms: [target('instagram', { publishStatus: 'published' })] }),
  post('p2', { title: 'Launch day blog', contentType: 'blog_post', status: 'draft', createdAt: '2026-11-05T08:00:00.0000000+00:00' }),
  post('p3', { title: 'Weekend promo', status: 'scheduled', scheduledAt: '2026-11-20T09:00:00.0000000+00:00', createdAt: '2026-11-04T08:00:00.0000000+00:00', platforms: [target('x', { publishStatus: 'scheduled' })] }),
  post('p4', { title: 'Old news', status: 'archived', createdAt: '2026-11-03T08:00:00.0000000+00:00' }),
];

function open(options: ContentServerOptions = {}, path = '/content-studio', overrides = {}) {
  const server = contentServer({ posts: sample(), ...options }, overrides);
  return renderApp(path, server.routes).then((view) => ({ ...view, server }));
}

const row = (title: string) => screen.getByRole('link', { name: title }).closest('tr') as HTMLElement;

describe('the Content Studio list', () => {
  it('lists the posts with their status, platforms and the date they were made, newest first', async () => {
    await open();

    expect(await heading('Content Studio')).toBeTruthy();
    const table = await screen.findByRole('table');
    const titles = within(table).getAllByRole('link', { name: /motivation|blog|promo|news/i }).map((link) => link.textContent);
    expect(titles).toEqual(['Morning motivation', 'Launch day blog', 'Weekend promo', 'Old news']);

    expect(within(row('Morning motivation')).getByText('Published')).toBeTruthy();
    expect(within(row('Launch day blog')).getByText('Draft')).toBeTruthy();
    expect(within(row('Weekend promo')).getByText(/^for /)).toBeTruthy();
    expect(within(row('Morning motivation')).getByRole('list', { name: 'Platforms' })).toBeTruthy();
    // Nothing keeps statistics yet, and the page says so rather than showing zeros.
    expect(within(row('Morning motivation')).getByTitle('Statistics are not available yet')).toBeTruthy();
  });

  it('shows the count of each kind in its tab, and keeps them while one kind is open', async () => {
    await open();
    const tabs = await screen.findByRole('navigation', { name: 'Kind of content' });

    expect(within(tabs).getByRole('link', { name: /All Content\s*\(4\)/ })).toBeTruthy();
    expect(within(tabs).getByRole('link', { name: /Social Posts\s*\(3\)/ })).toBeTruthy();
    expect(within(tabs).getByRole('link', { name: /Blog Posts\s*\(1\)/ })).toBeTruthy();

    fireEvent.click(within(tabs).getByRole('link', { name: /Blog Posts/ }));
    await waitFor(() => expect(location()).toBe('/content-studio?type=blog_post'));
    expect(await screen.findByRole('link', { name: 'Launch day blog' })).toBeTruthy();
    expect(screen.queryByRole('link', { name: 'Morning motivation' })).toBeNull();
    expect(within(screen.getByRole('navigation', { name: 'Kind of content' })).getByRole('link', { name: /Social Posts\s*\(3\)/ })).toBeTruthy();
    expect(within(screen.getByRole('navigation', { name: 'Kind of content' })).getByRole('link', { name: /Blog Posts/ }).getAttribute('aria-current')).toBe('page');
  });

  it('puts the filters in the address and reads them back from it', async () => {
    await open({}, '/content-studio?status=draft&type=blog_post');

    expect(await screen.findByRole('link', { name: 'Launch day blog' })).toBeTruthy();
    expect(screen.queryByRole('link', { name: 'Weekend promo' })).toBeNull();
    expect((screen.getByLabelText('Status') as HTMLSelectElement).value).toBe('draft');
  });

  it('ignores what the address holds that the list does not know', async () => {
    const { server } = await open({}, '/content-studio?status=zzz&type=podcast&page=-4&platform=myspace');

    expect(await screen.findByRole('link', { name: 'Old news' })).toBeTruthy();
    expect(server.requests.filter((line) => line.startsWith('GET /v1/content/posts?'))).toEqual(['GET /v1/content/posts?page=1&pageSize=10']);
  });

  it('narrows by status and platform through the address, and Reset undoes it but keeps the tab', async () => {
    await open({}, '/content-studio?type=social_post');
    await screen.findByRole('link', { name: 'Weekend promo' });

    fireEvent.change(screen.getByLabelText('Status'), { target: { value: 'scheduled' } });
    await waitFor(() => expect(location()).toBe('/content-studio?type=social_post&status=scheduled'));
    await waitFor(() => expect(screen.queryByRole('link', { name: 'Morning motivation' })).toBeNull());
    expect(screen.getByRole('link', { name: 'Weekend promo' })).toBeTruthy();

    fireEvent.change(screen.getByLabelText('Platform'), { target: { value: 'x' } });
    await waitFor(() => expect(location()).toBe('/content-studio?type=social_post&status=scheduled&platform=x'));

    fireEvent.click(screen.getByRole('button', { name: 'Reset' }));
    await waitFor(() => expect(location()).toBe('/content-studio?type=social_post'));
    expect(await screen.findByRole('link', { name: 'Morning motivation' })).toBeTruthy();
  });

  it('marks the platforms the organization has not enabled, and switches the campaign filter off until there are campaigns', async () => {
    await open();
    await screen.findByRole('table');

    const platforms = within(screen.getByLabelText('Platform') as HTMLSelectElement).getAllByRole('option').map((option) => option.textContent);
    expect(platforms).toContain('Instagram');
    expect(platforms).toContain('Facebook (not enabled)');
    expect((screen.getByLabelText('Campaign') as HTMLSelectElement).disabled).toBe(true);
  });

  it('searches after the typing pauses, and puts the words in the address', async () => {
    const { server } = await open();
    await screen.findByRole('table');

    fireEvent.change(screen.getByRole('searchbox', { name: 'Search content' }), { target: { value: 'launch' } });
    // Not at every keystroke: nothing has been asked for yet.
    expect(server.requests.some((line) => line.includes('q=launch'))).toBe(false);

    await waitFor(() => expect(location()).toBe('/content-studio?q=launch'));
    expect(await screen.findByRole('link', { name: 'Launch day blog' })).toBeTruthy();
    await waitFor(() => expect(screen.queryByRole('link', { name: 'Old news' })).toBeNull());
  });

  it('says so when nothing matches, and offers Reset filters', async () => {
    await open({}, '/content-studio?q=zebra');

    expect(await screen.findByText('No content matches what you chose.')).toBeTruthy();
    fireEvent.click(screen.getByRole('button', { name: 'Reset filters' }));
    expect(await screen.findByRole('link', { name: 'Old news' })).toBeTruthy();
  });

  it('says there is no content yet, and points a writer at the button', async () => {
    await open({ posts: [] });

    expect(await screen.findByText('There is no content yet.')).toBeTruthy();
    expect(screen.getByText(/Create New Content/, { selector: 'p' })).toBeTruthy();
  });

  it('does not point a reader at a button they do not have', async () => {
    await open({ posts: [], permissions: reader });

    expect(await screen.findByText('There is no content yet.')).toBeTruthy();
    expect(screen.queryByText(/Use “Create New Content”/)).toBeNull();
    expect(screen.queryByRole('button', { name: /Create New Content/ })).toBeNull();
  });

  it('pages through the list ten at a time', async () => {
    const many = Array.from({ length: 12 }, (_, i) =>
      post(`m${i}`, { title: `Entry ${String(i).padStart(2, '0')}`, createdAt: `2026-10-${String(i + 10)}T08:00:00.0000000+00:00` }),
    );
    await open({ posts: many });

    await screen.findByRole('link', { name: 'Entry 11' });
    expect(screen.queryByRole('link', { name: 'Entry 00' })).toBeNull();
    expect(screen.getByText(/1–10 of 12/)).toBeTruthy();

    fireEvent.click(screen.getByRole('button', { name: 'Next' }));
    await waitFor(() => expect(location()).toBe('/content-studio?page=2'));
    expect(await screen.findByRole('link', { name: 'Entry 00' })).toBeTruthy();
    expect(screen.getByText(/11–12 of 12/)).toBeTruthy();
    expect((screen.getByRole('button', { name: 'Next' }) as HTMLButtonElement).disabled).toBe(true);
  });

  it('shows the error and tries again', async () => {
    let failing = true;
    const server = contentServer({ posts: sample() });
    const real = server.routes['GET /v1/content/posts?page=1&pageSize=10'];
    const faulty = contentServer({ posts: sample() }, {
      'GET /v1/content/posts?page=1&pageSize=10': (call) => (failing ? jsonResponse(500, { code: 'server_error' }) : (real as (c: typeof call) => Response)(call)),
    });
    await renderApp('/content-studio', faulty.routes);

    expect(await screen.findByRole('alert')).toBeTruthy();
    failing = false;
    fireEvent.click(screen.getByRole('button', { name: 'Try again' }));
    expect(await screen.findByRole('link', { name: 'Old news' })).toBeTruthy();
  });

  describe('Create New Content', () => {
    it('opens the five kinds, each a link to the Create page for that kind', async () => {
      await open({ permissions: writer });
      await screen.findByRole('table');

      fireEvent.click(screen.getByRole('button', { name: /Create New Content/ }));
      const menu = screen.getByRole('menu', { name: 'Kind of content' });
      const links = within(menu).getAllByRole('menuitem');
      expect(links.map((link) => link.getAttribute('href'))).toEqual([
        '/content-studio/new?type=social_post',
        '/content-studio/new?type=blog_post',
        '/content-studio/new?type=video_script',
        '/content-studio/new?type=carousel',
        '/content-studio/new?type=short_reel',
      ]);
    });

    it('closes with Escape and gives focus back to the button', async () => {
      await open({ permissions: writer });
      await screen.findByRole('table');

      const button = screen.getByRole('button', { name: /Create New Content/ });
      fireEvent.click(button);
      fireEvent.keyDown(document, { key: 'Escape' });

      expect(screen.queryByRole('menu', { name: 'Kind of content' })).toBeNull();
      expect(document.activeElement).toBe(button);
    });

    it('is not there for someone who cannot create', async () => {
      await open({ permissions: reader });
      await screen.findByRole('table');

      expect(screen.queryByRole('button', { name: /Create New Content/ })).toBeNull();
    });
  });

  describe('what each row offers', () => {
    const actionsOf = async (title: string) => {
      fireEvent.click(within(row(title)).getByRole('button', { name: `Actions for ${title}` }));
      return within(screen.getByRole('menu', { name: `Actions for ${title}` }))
        .getAllByRole('menuitem')
        .map((item) => item.textContent);
    };

    it('a reader can only open', async () => {
      await open({ permissions: reader });
      await screen.findByRole('table');

      expect(await actionsOf('Launch day blog')).toEqual(['Open']);
    });

    it('a writer can edit, copy and delete a draft, but not archive it or touch what is scheduled', async () => {
      await open({ permissions: writer });
      await screen.findByRole('table');

      expect(await actionsOf('Launch day blog')).toEqual(['Edit', 'Duplicate', 'Delete']);
      fireEvent.keyDown(document, { key: 'Escape' });
      expect(await actionsOf('Weekend promo')).toEqual(['Edit', 'Duplicate']);
    });

    it('a publisher can also unschedule, archive and restore', async () => {
      await open({ permissions: allContentPermissions });
      await screen.findByRole('table');

      expect(await actionsOf('Weekend promo')).toEqual(['Edit', 'Duplicate', 'Unschedule']);
      fireEvent.keyDown(document, { key: 'Escape' });
      expect(await actionsOf('Morning motivation')).toEqual(['Open', 'Duplicate', 'Archive']);
      fireEvent.keyDown(document, { key: 'Escape' });
      expect(await actionsOf('Old news')).toEqual(['Open', 'Duplicate', 'Restore', 'Delete']);
    });

    it('Edit goes to the post', async () => {
      await open({ permissions: writer });
      await screen.findByRole('table');
      await actionsOf('Launch day blog');

      expect(screen.getByRole('menuitem', { name: 'Edit' }).getAttribute('href')).toBe('/content-studio/p2');
    });
  });

  describe('doing something to a post', () => {
    it('archives it, says so, and shows the list as it is now', async () => {
      const { server } = await open({ permissions: allContentPermissions });
      await screen.findByRole('table');

      fireEvent.click(within(row('Morning motivation')).getByRole('button', { name: 'Actions for Morning motivation' }));
      fireEvent.click(screen.getByRole('menuitem', { name: 'Archive' }));

      expect(await screen.findByText('“Morning motivation” was archived.')).toBeTruthy();
      expect(server.requests).toContain('POST /v1/content/posts/p1/archive');
      await waitFor(() => expect(within(row('Morning motivation')).getByText('Archived')).toBeTruthy());

      fireEvent.click(screen.getByRole('button', { name: 'Dismiss' }));
      expect(screen.queryByText('“Morning motivation” was archived.')).toBeNull();
    });

    it('takes a scheduled post back to a draft', async () => {
      const { server } = await open({ permissions: allContentPermissions });
      await screen.findByRole('table');

      fireEvent.click(within(row('Weekend promo')).getByRole('button', { name: 'Actions for Weekend promo' }));
      fireEvent.click(screen.getByRole('menuitem', { name: 'Unschedule' }));

      expect(await screen.findByText('“Weekend promo” was taken back to a draft.')).toBeTruthy();
      expect(server.requests).toContain('POST /v1/content/posts/p3/unschedule');
      await waitFor(() => expect(within(row('Weekend promo')).getByText('Draft')).toBeTruthy());
    });

    it('restores an archived post', async () => {
      const { server } = await open({ permissions: allContentPermissions });
      await screen.findByRole('table');

      fireEvent.click(within(row('Old news')).getByRole('button', { name: 'Actions for Old news' }));
      fireEvent.click(screen.getByRole('menuitem', { name: 'Restore' }));

      expect(await screen.findByText('“Old news” was restored.')).toBeTruthy();
      expect(server.requests).toContain('POST /v1/content/posts/p4/restore');
    });

    it('duplicates it and opens the copy', async () => {
      const { server } = await open({ permissions: writer });
      await screen.findByRole('table');

      fireEvent.click(within(row('Launch day blog')).getByRole('button', { name: 'Actions for Launch day blog' }));
      fireEvent.click(screen.getByRole('menuitem', { name: 'Duplicate' }));

      await waitFor(() => expect(location()).toMatch(/^\/content-studio\/copy-\d+$/));
      expect(server.requests).toContain('POST /v1/content/posts/p2/duplicate');
    });

    it('asks before deleting, and does nothing if the person backs out', async () => {
      const { server } = await open({ permissions: writer });
      await screen.findByRole('table');

      fireEvent.click(within(row('Launch day blog')).getByRole('button', { name: 'Actions for Launch day blog' }));
      fireEvent.click(screen.getByRole('menuitem', { name: 'Delete' }));

      const dialog = await screen.findByRole('dialog', { name: 'Delete this post?' });
      expect(within(dialog).getByText('Launch day blog')).toBeTruthy();
      // Focus starts on the harmless button.
      expect(document.activeElement).toBe(within(dialog).getByRole('button', { name: 'Cancel' }));

      fireEvent.click(within(dialog).getByRole('button', { name: 'Cancel' }));
      expect(screen.queryByRole('dialog')).toBeNull();
      expect(server.requests.some((line) => line.startsWith('DELETE'))).toBe(false);
    });

    it('deletes after the person confirms', async () => {
      const { server } = await open({ permissions: writer });
      await screen.findByRole('table');

      fireEvent.click(within(row('Launch day blog')).getByRole('button', { name: 'Actions for Launch day blog' }));
      fireEvent.click(screen.getByRole('menuitem', { name: 'Delete' }));
      fireEvent.click(within(await screen.findByRole('dialog')).getByRole('button', { name: 'Delete' }));

      expect(await screen.findByText('The post was deleted.')).toBeTruthy();
      expect(server.requests).toContain('DELETE /v1/content/posts/p2');
      await waitFor(() => expect(screen.queryByRole('link', { name: 'Launch day blog' })).toBeNull());
    });

    it('keeps the dialog open and says why when the server refuses', async () => {
      await open({ permissions: writer }, '/content-studio', {
        'DELETE /v1/content/posts/p2': () => jsonResponse(409, { code: 'post_not_deletable', detail: 'x' }),
      });
      await screen.findByRole('table');

      fireEvent.click(within(row('Launch day blog')).getByRole('button', { name: 'Actions for Launch day blog' }));
      fireEvent.click(screen.getByRole('menuitem', { name: 'Delete' }));
      fireEvent.click(within(await screen.findByRole('dialog')).getByRole('button', { name: 'Delete' }));

      const dialog = await screen.findByRole('dialog');
      expect((await within(dialog).findByRole('alert')).textContent).toMatch(/cannot be deleted/i);
      expect(screen.getByRole('link', { name: 'Launch day blog' })).toBeTruthy();
    });

    it('says why when an action is refused', async () => {
      await open({ permissions: allContentPermissions }, '/content-studio', {
        'POST /v1/content/posts/p1/archive': () => jsonResponse(409, { code: 'invalid_transition', detail: 'x' }),
      });
      await screen.findByRole('table');

      fireEvent.click(within(row('Morning motivation')).getByRole('button', { name: 'Actions for Morning motivation' }));
      fireEvent.click(screen.getByRole('menuitem', { name: 'Archive' }));

      expect(await screen.findByRole('alert')).toBeTruthy();
      expect(within(row('Morning motivation')).getByText('Published')).toBeTruthy();
    });
  });

  describe('the workspace filter', () => {
    it('is not there for an organization with one workspace', async () => {
      await open();
      await screen.findByRole('table');

      expect(screen.queryByLabelText(/^All (products|clients|workspaces)$/i)).toBeNull();
    });

    it('is there for a company with several, in its own word', async () => {
      await open({
        workspaces: [
          { id: '11111111-1111-1111-1111-111111111111', name: 'Soap', createdAt: '2026-03-05T10:30:00Z' },
          { id: '22222222-2222-2222-2222-222222222222', name: 'Candles', createdAt: '2026-03-05T10:30:00Z' },
        ],
      });
      await screen.findByRole('table');

      const select = screen.getByLabelText('All products') as HTMLSelectElement;
      expect(within(select).getAllByRole('option').map((option) => option.textContent)).toEqual(['All products', 'Soap', 'Candles']);
    });
  });

  it('does not ask for anything without contentstudio.view', async () => {
    const server = contentServer({ posts: sample(), permissions: [PERMISSIONS.dashboardView] }, {});
    const { fetchFn } = await renderApp('/content-studio', server.routes);

    expect(await screen.findByText(/do not have access/i)).toBeTruthy();
    expect(fetchFn.calls.map(requestLine).some((line) => line.includes('/v1/content'))).toBe(false);
  });
});
