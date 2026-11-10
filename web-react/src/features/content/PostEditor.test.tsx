import { fireEvent, screen, waitFor, within } from '@testing-library/react';
import { useImperativeHandle, type Ref } from 'react';
import { describe, expect, it, vi } from 'vitest';
import type { RichDoc } from '@/api/content';
import { PERMISSIONS } from '@/access/permissions';
import { jsonResponse, requestLine } from '@/test/helpers';
import { allContentPermissions, contentServer, post, target, template, type ContentServerOptions } from '@/test/content';
import { renderApp } from '@/test/renderApp';
import { docFromText, plainText } from './richText/doc';

// Tiptap needs a real browser's selection and layout to be typed into; its own tests are in RichTextEditor.test.tsx. Here the editor
// is a plain text box that speaks the same language (a document in, a document out), so these tests are about the page around it.
vi.mock('./richText/RichTextEditor', () => ({
  RichTextEditor: ({
    value,
    onChange,
    disabled,
    labelledBy,
    handleRef,
  }: {
    value: RichDoc;
    onChange: (doc: RichDoc) => void;
    disabled?: boolean;
    labelledBy: string;
    handleRef?: Ref<{ insertText: (text: string) => void; focus: () => void; toggleEmoji: () => void }>;
  }) => {
    useImperativeHandle(handleRef, () => ({
      insertText: (text: string) => onChange(docFromText(`${plainText(value)}${text}`)),
      focus: () => undefined,
      toggleEmoji: () => undefined,
    }));
    return (
      <textarea
        aria-labelledby={labelledBy}
        disabled={disabled}
        value={plainText(value)}
        onChange={(event) => onChange(docFromText(event.target.value))}
      />
    );
  },
}));

const location = () => screen.getByTestId('location').textContent;
const writer = [PERMISSIONS.dashboardView, PERMISSIONS.workspacesView, PERMISSIONS.contentStudioView, PERMISSIONS.contentStudioCreate];
const reader = [PERMISSIONS.dashboardView, PERMISSIONS.contentStudioView];

function open(path: string, options: ContentServerOptions = {}, overrides = {}) {
  const server = contentServer(options, overrides);
  return renderApp(path, server.routes).then((view) => ({ ...view, server }));
}

const editorBox = () => screen.findByRole('textbox', { name: 'Post Editor' });
const type = async (text: string) => fireEvent.change(await editorBox(), { target: { value: text } });
const actionBar = () => within(screen.getByRole('group', { name: 'Post actions' }));
const choose = (platform: string) => fireEvent.click(screen.getByRole('button', { name: platform }));
const isDisabled = (element: HTMLElement) => element.matches(':disabled');

describe('the Create page', () => {
  it('opens for the kind of content chosen, with the three steps, media off for a role that cannot use the library, and the AI assistant not built', async () => {
    await open('/content-studio/new?type=blog_post');

    expect(await screen.findByRole('heading', { level: 1, name: 'Create New Post' })).toBeTruthy();
    const steps = within(screen.getByRole('navigation', { name: 'Steps' }));
    expect(steps.getByRole('button', { name: /Create Content/ }).getAttribute('aria-current')).toBe('step');
    expect(steps.getByRole('button', { name: /Customize/ })).toBeTruthy();
    expect(steps.getByRole('button', { name: /Schedule & Publish/ })).toBeTruthy();

    expect(screen.getByRole('button', { name: 'Blog Post' }).getAttribute('aria-pressed')).toBe('true');
    expect(screen.getByRole('button', { name: 'Social Post' }).getAttribute('aria-pressed')).toBe('false');

    // This person's token does not carry medialibrary.view, so the media panel is off and says why (the library itself is tested in
    // PostEditorMedia.test.tsx); the AI assistant does not exist yet: shown, switched off, and said so.
    expect(isDisabled(screen.getByRole('button', { name: 'Upload' }))).toBe(true);
    expect(screen.getByText('Your role cannot use the Media Library, so files cannot be added.')).toBeTruthy();
    expect(isDisabled(screen.getByRole('textbox', { name: 'Ask the AI assistant' }))).toBe(true);
    expect(screen.getByText('The AI assistant is coming soon.')).toBeTruthy();
    expect(isDisabled(screen.getByLabelText(/^Campaign/))).toBe(true);
  });

  it('falls back to a social post when the address names a kind that does not exist', async () => {
    await open('/content-studio/new?type=podcast');

    await screen.findByRole('heading', { level: 1, name: 'Create New Post' });
    expect(screen.getByRole('button', { name: 'Social Post' }).getAttribute('aria-pressed')).toBe('true');
  });

  it('offers only the platforms the Site admin enabled', async () => {
    await open('/content-studio/new');
    await screen.findByRole('heading', { level: 1, name: 'Create New Post' });

    const settings = within(screen.getByRole('region', { name: 'Post Settings' }));
    const platforms = settings.getAllByRole('button').filter((button) => button.hasAttribute('aria-pressed')).map((button) => button.textContent);
    expect(platforms).toEqual(['Instagram', 'X', 'YouTube']);
  });

  it('has nothing to save until there is a title or some text', async () => {
    await open('/content-studio/new');
    await screen.findByRole('heading', { level: 1, name: 'Create New Post' });

    expect(isDisabled(actionBar().getByRole('button', { name: 'Save Draft' }))).toBe(true);
    await type('Hello');
    expect(isDisabled(actionBar().getByRole('button', { name: 'Save Draft' }))).toBe(false);
  });

  it('saves a new post as a draft: sends the document, then the address names the post', async () => {
    const { server } = await open('/content-studio/new?type=blog_post');
    await screen.findByRole('heading', { level: 1, name: 'Create New Post' });

    fireEvent.change(screen.getByLabelText('Title'), { target: { value: '  My plan  ' } });
    await type('First line');
    choose('Instagram');
    fireEvent.click(actionBar().getByRole('button', { name: 'Save Draft' }));

    expect(await screen.findByText('Draft saved.')).toBeTruthy();
    await waitFor(() => expect(location()).toBe('/content-studio/new-1'));
    expect(await screen.findByRole('heading', { level: 1, name: 'Edit Post' })).toBeTruthy();

    const sent = server.bodies.find((entry) => entry.route === 'POST /v1/content/posts')?.body as Record<string, unknown>;
    expect(sent).toMatchObject({
      workspaceId: 'w1',
      contentType: 'blog_post',
      title: 'My plan',
      visibility: 'public',
      platforms: [{ platform: 'instagram', caption: null, title: null, settings: {} }],
    });
    expect(plainText(sent.bodyDoc as RichDoc)).toBe('First line');
    expect(sent).not.toHaveProperty('expectedUpdatedAt');
    // The post it made is read once, on the page that follows, not twice.
    expect(server.requests.filter((line) => line.startsWith('POST /v1/content/posts'))).toEqual(['POST /v1/content/posts']);
    expect(server.requests).not.toContain('GET /v1/content/posts/new-1');
  });

  it('says what the server found wrong, under the field it is about', async () => {
    await open('/content-studio/new', {}, {
      'POST /v1/content/posts': () =>
        jsonResponse(400, { code: 'validation_failed', detail: 'x', errors: { title: ['The title is too long.'] } }),
    });
    await screen.findByRole('heading', { level: 1, name: 'Create New Post' });

    await type('Hello');
    fireEvent.click(actionBar().getByRole('button', { name: 'Save Draft' }));

    const message = await screen.findByText('The title is too long.', { selector: '.form-error' });
    expect(message.getAttribute('role')).toBe('alert');
    expect(screen.getByLabelText('Title').getAttribute('aria-describedby')).toBe(message.id);
    // Still on the Create page, with what was written.
    expect(location()).toBe('/content-studio/new');
    expect((await editorBox() as HTMLTextAreaElement).value).toBe('Hello');
  });

  it('adds hashtags and tags as chips, once each, and takes them away again', async () => {
    const { server } = await open('/content-studio/new');
    await screen.findByRole('heading', { level: 1, name: 'Create New Post' });

    const hashtags = screen.getByLabelText('Hashtags');
    fireEvent.change(hashtags, { target: { value: '#fun' } });
    fireEvent.keyDown(hashtags, { key: 'Enter' });
    fireEvent.change(hashtags, { target: { value: 'FUN' } });
    fireEvent.keyDown(hashtags, { key: 'Enter' });
    fireEvent.change(hashtags, { target: { value: '#two #three ' } });

    const list = within(screen.getByRole('list', { name: 'Hashtags' }));
    expect(list.getAllByRole('listitem').map((item) => item.textContent?.replace('×', ''))).toEqual(['#fun', '#two', '#three']);

    fireEvent.click(list.getByRole('button', { name: 'Remove #two' }));
    expect(within(screen.getByRole('list', { name: 'Hashtags' })).queryByText('#two')).toBeNull();

    const tags = screen.getByLabelText('Tags');
    fireEvent.change(tags, { target: { value: 'Launch' } });
    fireEvent.keyDown(tags, { key: 'Enter' });
    expect(within(screen.getByRole('list', { name: 'Tags' })).getByText('Launch')).toBeTruthy();

    await type('Body');
    fireEvent.click(actionBar().getByRole('button', { name: 'Save Draft' }));
    await screen.findByText('Draft saved.');
    const sent = server.bodies.find((entry) => entry.route === 'POST /v1/content/posts')?.body as Record<string, unknown>;
    expect(sent.hashtags).toEqual(['fun', 'three']);
    expect(sent.tags).toEqual(['Launch']);
  });

  it('asks the server for hashtag suggestions for the workspace, and tags', async () => {
    const { server } = await open('/content-studio/new');
    await screen.findByRole('heading', { level: 1, name: 'Create New Post' });

    await waitFor(() => expect(server.requests.some((line) => line.startsWith('GET /v1/content/hashtags/suggestions?'))).toBe(true));
    expect(server.requests.some((line) => line.startsWith('GET /v1/content/tags?workspaceId=w1'))).toBe(true);
  });

  it('does not ask for the workspace when there is one, and does when there are several', async () => {
    await open('/content-studio/new');
    await screen.findByRole('heading', { level: 1, name: 'Create New Post' });
    expect(screen.queryByLabelText('Product')).toBeNull();
  });

  it('lets a company with several workspaces choose which product the post is for, and sends it', async () => {
    const { server } = await open('/content-studio/new', {
      workspaces: [
        { id: 'w1', name: 'Soap', createdAt: '2026-03-05T10:30:00Z' },
        { id: 'w2', name: 'Candles', createdAt: '2026-03-05T10:30:00Z' },
      ],
    });
    await screen.findByRole('heading', { level: 1, name: 'Create New Post' });

    fireEvent.change(screen.getByLabelText('Product'), { target: { value: 'w2' } });
    await type('Hello');
    fireEvent.click(actionBar().getByRole('button', { name: 'Save Draft' }));
    await screen.findByText('Draft saved.');

    expect((server.bodies.find((entry) => entry.route === 'POST /v1/content/posts')?.body as Record<string, unknown>).workspaceId).toBe('w2');
  });

  it('starts in the workspace the list was showing', async () => {
    await open('/content-studio/new?workspace=w2', {
      workspaces: [
        { id: 'w1', name: 'Soap', createdAt: '2026-03-05T10:30:00Z' },
        { id: 'w2', name: 'Candles', createdAt: '2026-03-05T10:30:00Z' },
      ],
    });
    await screen.findByRole('heading', { level: 1, name: 'Create New Post' });

    expect((screen.getByLabelText('Product') as HTMLSelectElement).value).toBe('w2');
  });

  it('shows a live preview of what is written', async () => {
    await open('/content-studio/new');
    await screen.findByRole('heading', { level: 1, name: 'Create New Post' });

    await type('Hello preview');
    const preview = screen.getByRole('region', { name: /preview/i });
    expect(within(preview).getByText('Hello preview')).toBeTruthy();
  });

  it('marks Content Studio in the sidebar while the page is open', async () => {
    await open('/content-studio/new');
    await screen.findByRole('heading', { level: 1, name: 'Create New Post' });

    const link = within(screen.getByRole('navigation', { name: 'Main' })).getByRole('link', { name: 'Content Studio' });
    expect(link.getAttribute('aria-current')).toBe('page');
  });

  it('is not opened by someone who cannot create, and asks the server for nothing', async () => {
    const server = contentServer({ permissions: reader });
    const { fetchFn } = await renderApp('/content-studio/new', server.routes);

    expect(await screen.findByText(/do not have access/i)).toBeTruthy();
    expect(fetchFn.calls.map(requestLine).some((line) => line.includes('/v1/content'))).toBe(false);
  });
});

describe('the steps', () => {
  it('Customize needs a platform first, and then gives each its own text with a counter', async () => {
    await open('/content-studio/new');
    await screen.findByRole('heading', { level: 1, name: 'Create New Post' });

    fireEvent.click(within(screen.getByRole('navigation', { name: 'Steps' })).getByRole('button', { name: /Customize/ }));
    expect(screen.getByText(/Choose at least one platform first/)).toBeTruthy();
    fireEvent.click(screen.getByRole('button', { name: 'Choose platforms' }));

    await type('Short text');
    choose('X');
    fireEvent.click(within(screen.getByRole('navigation', { name: 'Steps' })).getByRole('button', { name: /Customize/ }));

    expect(screen.getByText('10 / 280')).toBeTruthy();
    fireEvent.change(screen.getByLabelText('Text for X'), { target: { value: 'x'.repeat(300) } });
    expect(screen.getByText('300 / 280')).toBeTruthy();
    expect(screen.getByText('— 20 too many')).toBeTruthy();
    expect(within(screen.getByLabelText('What X will show')).getByText('x'.repeat(300))).toBeTruthy();

    fireEvent.click(screen.getByRole('button', { name: 'Use the post’s text' }));
    expect(screen.getByText('10 / 280')).toBeTruthy();
  });

  it('only a platform that has a title gets a title field', async () => {
    await open('/content-studio/new');
    await screen.findByRole('heading', { level: 1, name: 'Create New Post' });

    await type('Text');
    choose('X');
    choose('YouTube');
    fireEvent.click(within(screen.getByRole('navigation', { name: 'Steps' })).getByRole('button', { name: /Customize/ }));

    expect(screen.getByLabelText('Title for YouTube')).toBeTruthy();
    expect(screen.queryByLabelText('Title for X')).toBeNull();
  });

  it('keeps the platform\'s own text when the post is saved', async () => {
    const { server } = await open('/content-studio/new');
    await screen.findByRole('heading', { level: 1, name: 'Create New Post' });

    await type('Main text');
    choose('X');
    fireEvent.click(within(screen.getByRole('navigation', { name: 'Steps' })).getByRole('button', { name: /Customize/ }));
    fireEvent.change(screen.getByLabelText('Text for X'), { target: { value: 'Own text for X' } });
    fireEvent.click(actionBar().getByRole('button', { name: 'Save Draft' }));
    await screen.findByText('Draft saved.');

    const sent = server.bodies.find((entry) => entry.route === 'POST /v1/content/posts')?.body as { platforms: unknown[] };
    expect(sent.platforms).toEqual([{ platform: 'x', caption: 'Own text for X', title: null, settings: {} }]);
  });
});

describe('Schedule & Publish', () => {
  const goToReview = () => fireEvent.click(actionBar().getByRole('button', { name: 'Schedule & Publish →' }));

  it('lists what is still wrong, each with the way back to fix it', async () => {
    await open('/content-studio/new');
    await screen.findByRole('heading', { level: 1, name: 'Create New Post' });

    goToReview();
    const problems = within(await screen.findByRole('alert'));
    expect(problems.getByText('Write some text before scheduling.')).toBeTruthy();
    expect(problems.getByText('Choose at least one platform.')).toBeTruthy();
    expect(isDisabled(screen.getByRole('button', { name: 'Publish now' }))).toBe(true);

    fireEvent.click(problems.getAllByRole('button', { name: 'Go to the post' })[0] as HTMLElement);
    expect(await screen.findByRole('heading', { name: 'Post Editor' })).toBeTruthy();
  });

  it('says when the text is longer than a platform takes, and points at Customize', async () => {
    await open('/content-studio/new');
    await screen.findByRole('heading', { level: 1, name: 'Create New Post' });

    await type('x'.repeat(300));
    choose('X');
    goToReview();

    expect(await screen.findByText('X: the text is 300 characters; the limit is 280.')).toBeTruthy();
    expect(screen.getByRole('button', { name: 'Go to Customize' })).toBeTruthy();
    expect(isDisabled(screen.getByRole('button', { name: 'Publish now' }))).toBe(true);
  });

  it('saves and publishes now, then goes back to the list and says so', async () => {
    const { server } = await open('/content-studio/new');
    await screen.findByRole('heading', { level: 1, name: 'Create New Post' });

    fireEvent.change(screen.getByLabelText('Title'), { target: { value: 'Big news' } });
    await type('Hello world');
    choose('Instagram');
    goToReview();
    fireEvent.click(screen.getByRole('button', { name: 'Publish now' }));

    await waitFor(() => expect(location()).toBe('/content-studio'));
    expect(await screen.findByText('“Big news” is being published.')).toBeTruthy();
    // Saved first, then published; one save.
    expect(server.requests.filter((line) => line.startsWith('POST /v1/content/posts'))).toEqual([
      'POST /v1/content/posts',
      'POST /v1/content/posts/new-1/publish-now',
    ]);
    expect(await screen.findByRole('link', { name: 'Big news' })).toBeTruthy();
  });

  it('schedules for a chosen time, and not for one that is too soon', async () => {
    const { server } = await open('/content-studio/new');
    await screen.findByRole('heading', { level: 1, name: 'Create New Post' });

    await type('Hello world');
    choose('Instagram');
    goToReview();
    fireEvent.click(screen.getByRole('radio', { name: 'Schedule for a date and time' }));

    // No time yet: nothing to schedule.
    expect(isDisabled(screen.getByRole('button', { name: 'Schedule' }))).toBe(true);

    fireEvent.change(screen.getByLabelText('Date and time'), { target: { value: '2020-01-10T09:00' } });
    expect(screen.getByText(/Choose a time at least a minute from now/)).toBeTruthy();
    expect(isDisabled(screen.getByRole('button', { name: /^Schedule for / }))).toBe(true);

    fireEvent.change(screen.getByLabelText('Date and time'), { target: { value: '2031-01-10T09:00' } });
    const button = screen.getByRole('button', { name: /^Schedule for / });
    expect(isDisabled(button)).toBe(false);
    fireEvent.click(button);

    await waitFor(() => expect(location()).toBe('/content-studio'));
    expect(await screen.findByText(/The post is scheduled for .*2031/)).toBeTruthy();
    const scheduled = server.bodies.find((entry) => entry.route === 'POST /v1/content/posts/new-1/schedule')?.body as { scheduledAt: string };
    expect(new Date(scheduled.scheduledAt).getFullYear()).toBe(2031);
  });

  it('does not leave the page, and says why, when the server refuses', async () => {
    await open('/content-studio/new', {}, {
      'POST /v1/content/posts/new-1/publish-now': () => jsonResponse(400, { code: 'platform_not_enabled', detail: 'x' }),
    });
    await screen.findByRole('heading', { level: 1, name: 'Create New Post' });

    await type('Hello world');
    choose('Instagram');
    goToReview();
    fireEvent.click(screen.getByRole('button', { name: 'Publish now' }));

    expect(await screen.findByText(/not enabled/i, { selector: '.notice-error p' })).toBeTruthy();
    expect(location()).not.toBe('/content-studio');
  });

  it('can save as a draft from the review step', async () => {
    const { server } = await open('/content-studio/new');
    await screen.findByRole('heading', { level: 1, name: 'Create New Post' });

    await type('Hello world');
    goToReview();
    fireEvent.click(screen.getByRole('button', { name: 'Save as draft' }));

    expect(await screen.findByText('Draft saved.')).toBeTruthy();
    expect(server.requests).toContain('POST /v1/content/posts');
  });

  it('a writer who cannot publish sees Review, not the buttons that publish', async () => {
    await open('/content-studio/new', { permissions: writer });
    await screen.findByRole('heading', { level: 1, name: 'Create New Post' });

    await type('Hello world');
    choose('Instagram');
    fireEvent.click(actionBar().getByRole('button', { name: 'Review →' }));

    expect(await screen.findByText(/Your role can write and save drafts/)).toBeTruthy();
    expect(screen.queryByRole('button', { name: 'Publish now' })).toBeNull();
    expect(screen.queryByRole('radio')).toBeNull();
    expect(screen.getByRole('button', { name: 'Save as draft' })).toBeTruthy();
  });
});

describe('editing a post', () => {
  const draftPost = () =>
    post('p2', {
      title: 'Launch day blog',
      contentType: 'blog_post',
      hashtags: ['launch'],
      tags: [{ id: 't1', name: 'Blog', color: '#1F7A55' }],
      platforms: [target('youtube')],
      updatedAt: '2026-11-08T10:00:01.0000000+00:00',
    });

  it('opens the post as it is, and asks the server for it once', async () => {
    const { server } = await open('/content-studio/p2', { posts: [draftPost()] });

    expect(await screen.findByRole('heading', { level: 1, name: 'Edit Post' })).toBeTruthy();
    expect((screen.getByLabelText('Title') as HTMLInputElement).value).toBe('Launch day blog');
    expect((await editorBox() as HTMLTextAreaElement).value).toBe('Text of p2');
    expect(screen.getByRole('button', { name: 'Blog Post' }).getAttribute('aria-pressed')).toBe('true');
    expect(screen.getByRole('button', { name: 'YouTube' }).getAttribute('aria-pressed')).toBe('true');
    expect(within(screen.getByRole('list', { name: 'Hashtags' })).getByText('#launch')).toBeTruthy();
    expect(screen.getByText('Draft', { selector: '.status-line *' })).toBeTruthy();
    expect(server.requests.filter((line) => line === 'GET /v1/content/posts/p2')).toHaveLength(1);
  });

  it('saves a change as a new version of the same post, carrying the version it started from', async () => {
    const { server } = await open('/content-studio/p2', { posts: [draftPost()] });
    await screen.findByRole('heading', { level: 1, name: 'Edit Post' });

    fireEvent.change(screen.getByLabelText('Title'), { target: { value: 'A better title' } });
    fireEvent.click(actionBar().getByRole('button', { name: 'Save Draft' }));

    expect(await screen.findByText('Draft saved.')).toBeTruthy();
    const put = server.bodies.find((entry) => entry.route === 'PUT /v1/content/posts/p2')?.body as Record<string, unknown>;
    expect(put).toMatchObject({ title: 'A better title', expectedUpdatedAt: '2026-11-08T10:00:01.0000000+00:00', hashtags: ['launch'], tags: ['Blog'] });
    expect(put).not.toHaveProperty('workspaceId');
    expect(server.requests.filter((line) => line.startsWith('POST /v1/content/posts'))).toEqual([]);
    expect(location()).toBe('/content-studio/p2');

    // The page now holds the newer version, so a second save is not a conflict with itself.
    fireEvent.change(screen.getByLabelText('Title'), { target: { value: 'Third title' } });
    fireEvent.click(actionBar().getByRole('button', { name: 'Save Draft' }));
    await waitFor(() => expect(server.requests.filter((line) => line === 'PUT /v1/content/posts/p2')).toHaveLength(2));
    expect(screen.queryByRole('alert')).toBeNull();
  });

  it('does not overwrite someone else\'s change: it says so and offers the latest version', async () => {
    const { server } = await open('/content-studio/p2', { posts: [draftPost()] }, {
      'PUT /v1/content/posts/p2': () => jsonResponse(409, { code: 'content_changed', detail: 'x' }),
    });
    await screen.findByRole('heading', { level: 1, name: 'Edit Post' });

    fireEvent.change(screen.getByLabelText('Title'), { target: { value: 'Mine' } });
    fireEvent.click(actionBar().getByRole('button', { name: 'Save Draft' }));

    const alert = await screen.findByRole('alert');
    expect(alert.textContent).toContain('changed after you opened it');
    fireEvent.click(within(alert).getByRole('button', { name: 'Reload the latest version' }));

    await waitFor(() => expect(server.requests.filter((line) => line === 'GET /v1/content/posts/p2')).toHaveLength(2));
    expect(await screen.findByRole('heading', { level: 1, name: 'Edit Post' })).toBeTruthy();
    expect((screen.getByLabelText('Title') as HTMLInputElement).value).toBe('Launch day blog');
  });

  it('says so when the post is not there', async () => {
    await open('/content-studio/missing', { posts: [draftPost()] });

    expect(await screen.findByText(/not found|could not be found|no longer exists/i)).toBeTruthy();
    expect(screen.getByRole('button', { name: 'Try again' })).toBeTruthy();
  });

  it('a scheduled post can be taken back to a draft by someone who may publish', async () => {
    const scheduled = post('p3', { status: 'scheduled', scheduledAt: '2030-05-01T09:00:00.0000000+00:00', platforms: [target('x', { publishStatus: 'scheduled' })] });
    const { server } = await open('/content-studio/p3', { posts: [scheduled] });
    await screen.findByRole('heading', { level: 1, name: 'Edit Post' });

    expect(screen.getByText(/Goes out/)).toBeTruthy();
    fireEvent.click(screen.getByRole('button', { name: 'Unschedule' }));

    // The page reads the post again after the change and starts from what the server holds now.
    expect(await screen.findByText('Draft', { selector: '.status-line *' })).toBeTruthy();
    expect(server.requests).toContain('POST /v1/content/posts/p3/unschedule');
    expect(server.requests.filter((line) => line === 'GET /v1/content/posts/p3')).toHaveLength(2);
    expect(screen.queryByRole('button', { name: 'Unschedule' })).toBeNull();
  });

  it('a writer who cannot publish is not offered Unschedule', async () => {
    const scheduled = post('p3', { status: 'scheduled', scheduledAt: '2030-05-01T09:00:00.0000000+00:00' });
    await open('/content-studio/p3', { posts: [scheduled], permissions: writer });
    await screen.findByRole('heading', { level: 1, name: 'Edit Post' });

    expect(screen.queryByRole('button', { name: 'Unschedule' })).toBeNull();
  });

  describe('a post that can no longer be edited', () => {
    const published = () =>
      post('p1', { status: 'published', publishedAt: '2026-11-06T09:00:00.0000000+00:00', platforms: [target('instagram', { publishStatus: 'published', externalPostUrl: 'https://example.com/p/1' })] });

    it('is shown read only, with its delivery status', async () => {
      await open('/content-studio/p1', { posts: [published()] });
      await screen.findByRole('heading', { level: 1, name: 'Edit Post' });

      expect(screen.getByText(/can no longer be edited/)).toBeTruthy();
      expect(isDisabled(screen.getByLabelText('Title'))).toBe(true);
      expect(isDisabled(await editorBox())).toBe(true);
      expect(isDisabled(actionBar().getByRole('button', { name: 'Save Draft' }))).toBe(true);
      expect(actionBar().queryByRole('button', { name: /Schedule & Publish|Review/ })).toBeNull();
      expect(screen.getByText('Instagram', { selector: '.delivery-list *, .delivery *' })).toBeTruthy();
    });

    it('offers Duplicate, and opens the copy', async () => {
      const { server } = await open('/content-studio/p1', { posts: [published()] });
      await screen.findByRole('heading', { level: 1, name: 'Edit Post' });

      fireEvent.click(screen.getByRole('button', { name: 'Duplicate' }));

      await waitFor(() => expect(location()).toMatch(/^\/content-studio\/copy-\d+$/));
      expect(server.requests).toContain('POST /v1/content/posts/p1/duplicate');
      expect(await screen.findByText('A copy was made as a draft.')).toBeTruthy();
      expect(await screen.findByText('Draft', { selector: '.status-line *' })).toBeTruthy();
      expect(isDisabled(screen.getByLabelText('Title'))).toBe(false);
    });

    it('lets a publisher archive it, and back to the list', async () => {
      const { server } = await open('/content-studio/p1', { posts: [published()] });
      await screen.findByRole('heading', { level: 1, name: 'Edit Post' });

      fireEvent.click(screen.getByRole('button', { name: 'Archive' }));

      await waitFor(() => expect(location()).toBe('/content-studio'));
      expect(server.requests).toContain('POST /v1/content/posts/p1/archive');
      expect(await screen.findByText('The post was archived.')).toBeTruthy();
    });

    it('lets a publisher restore an archived one', async () => {
      const archived = post('p4', { status: 'archived', archivedAt: '2026-11-07T09:00:00.0000000+00:00' });
      const { server } = await open('/content-studio/p4', { posts: [archived] });
      await screen.findByRole('heading', { level: 1, name: 'Edit Post' });

      fireEvent.click(screen.getByRole('button', { name: 'Restore' }));

      await waitFor(() => expect(server.requests).toContain('POST /v1/content/posts/p4/restore'));
    });

    it('does not offer a writer who cannot publish to archive', async () => {
      await open('/content-studio/p1', { posts: [published()], permissions: writer });
      await screen.findByRole('heading', { level: 1, name: 'Edit Post' });

      expect(screen.queryByRole('button', { name: 'Archive' })).toBeNull();
      expect(screen.getByRole('button', { name: 'Duplicate' })).toBeTruthy();
    });
  });

  it('someone who can only read sees the post and cannot change it', async () => {
    const { server } = await open('/content-studio/p2', { posts: [draftPost()], permissions: reader });
    await screen.findByRole('heading', { level: 1, name: 'Edit Post' });

    expect(screen.getByText('Your role can look at this post but not change it.')).toBeTruthy();
    expect(isDisabled(screen.getByLabelText('Title'))).toBe(true);
    expect(isDisabled(actionBar().getByRole('button', { name: 'Save Draft' }))).toBe(true);
    expect(screen.queryByRole('button', { name: 'Duplicate' })).toBeNull();
    expect(screen.queryByRole('button', { name: 'Save as template' })).toBeNull();
    expect(server.requests.some((line) => line.startsWith('PUT') || line.startsWith('POST /v1/content'))).toBe(false);
  });
});

describe('leaving with unsaved changes', () => {
  it('asks first, and the person can stay or leave', async () => {
    await open('/content-studio/new');
    await screen.findByRole('heading', { level: 1, name: 'Create New Post' });

    await type('Something I wrote');
    fireEvent.click(screen.getByRole('link', { name: '← Back to Content Studio' }));

    const dialog = await screen.findByRole('dialog', { name: 'Leave without saving?' });
    expect(location()).toBe('/content-studio/new');
    fireEvent.click(within(dialog).getByRole('button', { name: 'Keep editing' }));
    expect(screen.queryByRole('dialog')).toBeNull();
    expect((await editorBox() as HTMLTextAreaElement).value).toBe('Something I wrote');

    fireEvent.click(screen.getByRole('link', { name: '← Back to Content Studio' }));
    fireEvent.click(within(await screen.findByRole('dialog')).getByRole('button', { name: 'Leave' }));
    await waitFor(() => expect(location()).toBe('/content-studio'));
  });

  it('guards the sidebar too', async () => {
    await open('/content-studio/new');
    await screen.findByRole('heading', { level: 1, name: 'Create New Post' });

    await type('Something I wrote');
    fireEvent.click(within(screen.getByRole('navigation', { name: 'Main' })).getByRole('link', { name: 'Dashboard' }));

    expect(await screen.findByRole('dialog', { name: 'Leave without saving?' })).toBeTruthy();
    expect(location()).toBe('/content-studio/new');
  });

  it('does not ask when nothing was changed, or after saving', async () => {
    await open('/content-studio/new');
    await screen.findByRole('heading', { level: 1, name: 'Create New Post' });

    fireEvent.click(screen.getByRole('link', { name: '← Back to Content Studio' }));
    await waitFor(() => expect(location()).toBe('/content-studio'));
    expect(screen.queryByRole('dialog')).toBeNull();
  });

  it('does not ask after the draft was saved', async () => {
    await open('/content-studio/new');
    await screen.findByRole('heading', { level: 1, name: 'Create New Post' });

    await type('Hello');
    fireEvent.click(actionBar().getByRole('button', { name: 'Save Draft' }));
    await screen.findByText('Draft saved.');
    fireEvent.click(screen.getByRole('link', { name: '← Back to Content Studio' }));

    await waitFor(() => expect(location()).toBe('/content-studio'));
    expect(screen.queryByRole('dialog')).toBeNull();
  });

  it('asks the browser before the tab is closed, only while something is unsaved', async () => {
    await open('/content-studio/new');
    await screen.findByRole('heading', { level: 1, name: 'Create New Post' });

    const calm = new Event('beforeunload', { cancelable: true });
    window.dispatchEvent(calm);
    expect(calm.defaultPrevented).toBe(false);

    await type('Hello');
    const warned = new Event('beforeunload', { cancelable: true });
    window.dispatchEvent(warned);
    expect(warned.defaultPrevented).toBe(true);
  });
});

describe('templates', () => {
  it('starts from a template when nothing has been written', async () => {
    await open('/content-studio/new', { templates: [template('t1')] });
    await screen.findByRole('heading', { level: 1, name: 'Create New Post' });

    fireEvent.click(await screen.findByRole('button', { name: /Template t1/ }));

    expect((screen.getByLabelText('Title') as HTMLInputElement).value).toBe('Title t1');
    expect((await editorBox() as HTMLTextAreaElement).value).toBe('Body t1');
    expect(within(screen.getByRole('list', { name: 'Hashtags' })).getByText('#FromTemplate')).toBeTruthy();
    expect(screen.getByText('“Template t1” was applied. Save the draft to keep it.')).toBeTruthy();
  });

  it('asks before replacing what has been written', async () => {
    await open('/content-studio/new', { templates: [template('t1')] });
    await screen.findByRole('heading', { level: 1, name: 'Create New Post' });

    await type('My own words');
    fireEvent.click(await screen.findByRole('button', { name: /Template t1/ }));
    const dialog = await screen.findByRole('dialog', { name: 'Use this template?' });

    fireEvent.click(within(dialog).getByRole('button', { name: 'Cancel' }));
    expect((await editorBox() as HTMLTextAreaElement).value).toBe('My own words');

    fireEvent.click(screen.getByRole('button', { name: /Template t1/ }));
    fireEvent.click(within(await screen.findByRole('dialog')).getByRole('button', { name: 'Use template' }));
    expect((await editorBox() as HTMLTextAreaElement).value).toBe('Body t1');
  });

  it('only offers the templates of this kind of content', async () => {
    const { server } = await open('/content-studio/new?type=blog_post', {
      templates: [template('t1', { contentType: 'social_post' }), template('t2', { contentType: 'blog_post' })],
    });
    await screen.findByRole('heading', { level: 1, name: 'Create New Post' });

    expect(await screen.findByRole('button', { name: /Template t2/ })).toBeTruthy();
    expect(screen.queryByRole('button', { name: /Template t1/ })).toBeNull();
    expect(server.requests.some((line) => line.includes('/v1/content/templates?') && line.includes('type=blog_post'))).toBe(true);
  });

  it('says so when there are none', async () => {
    await open('/content-studio/new');

    expect(await screen.findByText('No social post templates yet.')).toBeTruthy();
  });

  it('saves what is written as a template, and then lists it', async () => {
    const { server } = await open('/content-studio/new');
    await screen.findByRole('heading', { level: 1, name: 'Create New Post' });

    expect(isDisabled(screen.getByRole('button', { name: 'Save as template' }))).toBe(true);
    fireEvent.change(screen.getByLabelText('Title'), { target: { value: 'Weekly tip' } });
    await type('A tip');
    fireEvent.click(screen.getByRole('button', { name: 'Save as template' }));

    const dialog = await screen.findByRole('dialog', { name: 'Save as template' });
    expect((within(dialog).getByLabelText('Template name') as HTMLInputElement).value).toBe('Weekly tip');
    fireEvent.click(within(dialog).getByRole('button', { name: 'Save template' }));

    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
    expect(await screen.findByRole('button', { name: /Weekly tip/ })).toBeTruthy();
    expect(server.templates).toHaveLength(1);
    expect(server.templates[0]).toMatchObject({ name: 'Weekly tip', contentType: 'social_post', title: 'Weekly tip' });
  });

  it('wants a name for the template', async () => {
    const { server } = await open('/content-studio/new');
    await screen.findByRole('heading', { level: 1, name: 'Create New Post' });

    await type('A tip');
    fireEvent.click(screen.getByRole('button', { name: 'Save as template' }));
    const dialog = await screen.findByRole('dialog', { name: 'Save as template' });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Save template' }));

    expect((await within(dialog).findByRole('alert')).textContent).toBe('Give the template a name.');
    expect(server.requests).not.toContain('POST /v1/content/templates');
  });

  it('a writer who cannot create is not offered Save as template, but a reader cannot use one either', async () => {
    await open('/content-studio/p2', { posts: [post('p2')], templates: [template('t1')], permissions: reader });
    await screen.findByRole('heading', { level: 1, name: 'Edit Post' });

    expect(screen.queryByRole('button', { name: 'Save as template' })).toBeNull();
    expect(isDisabled(await screen.findByRole('button', { name: /Template t1/ }))).toBe(true);
  });
});

describe('the extra options', () => {
  it('saves a poll, a button and a place that were filled in', async () => {
    const { server } = await open('/content-studio/new');
    await screen.findByRole('heading', { level: 1, name: 'Create New Post' });

    await type('Vote!');
    const more = within(screen.getByRole('region', { name: /more options/i }));
    fireEvent.click(more.getByRole('button', { name: /poll/i }));
    fireEvent.change(screen.getByLabelText(/Poll question|Question/i), { target: { value: 'Which?' } });
    const options = screen.getAllByLabelText(/^Option \d/);
    fireEvent.change(options[0] as HTMLElement, { target: { value: 'Red' } });
    fireEvent.change(options[1] as HTMLElement, { target: { value: 'Blue' } });
    fireEvent.click(actionBar().getByRole('button', { name: 'Save Draft' }));

    await screen.findByText('Draft saved.');
    const sent = server.bodies.find((entry) => entry.route === 'POST /v1/content/posts')?.body as { poll: unknown };
    expect(sent.poll).toEqual({ question: 'Which?', options: ['Red', 'Blue'], endsAt: null });
  });
});

describe('the whole route table', () => {
  it('opening a post needs only contentstudio.view, creating one needs contentstudio.create', async () => {
    const reading = contentServer({ posts: [post('p2')], permissions: reader });
    const first = await renderApp('/content-studio/p2', reading.routes);
    expect(await screen.findByRole('heading', { level: 1, name: 'Edit Post' })).toBeTruthy();
    first.unmount();

    const writing = contentServer({ permissions: allContentPermissions });
    await renderApp('/content-studio/new', writing.routes);
    expect(await screen.findByRole('heading', { level: 1, name: 'Create New Post' })).toBeTruthy();
  });
});
