import { fireEvent, screen, waitFor, within } from '@testing-library/react';
import { useImperativeHandle, type Ref } from 'react';
import { describe, expect, it, vi } from 'vitest';
import type { PostMediaItem, RichDoc } from '@/api/content';
import { PERMISSIONS } from '@/access/permissions';
import { jsonResponse } from '@/test/helpers';
import { allContentPermissions, contentServer, contentWithMediaPermissions, post, type ContentServerOptions } from '@/test/content';
import { asset, folder } from '@/test/media';
import { renderApp } from '@/test/renderApp';
import { docFromText, plainText } from './richText/doc';

// The editor is a plain text box here, as in PostEditor.test.tsx: these tests are about the files, not about Tiptap.
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
    useImperativeHandle(handleRef, () => ({ insertText: () => undefined, focus: () => undefined, toggleEmoji: () => undefined }));
    return <textarea aria-labelledby={labelledBy} disabled={disabled} value={plainText(value)} onChange={(event) => onChange(docFromText(event.target.value))} />;
  },
}));

const location = () => screen.getByTestId('location').textContent;
const heading = (name: string) => screen.findByRole('heading', { level: 1, name });

const beach = asset('a1', { workspaceId: 'w1', fileName: 'beach.png', sizeBytes: 2_000_000, width: 1600, height: 900, createdAt: '2026-11-07T09:00:00.0000000+00:00' });
const sunset = asset('a2', { workspaceId: 'w1', fileName: 'sunset.png', sizeBytes: 500_000, createdAt: '2026-11-06T09:00:00.0000000+00:00' });
const reel = asset('v1', { workspaceId: 'w1', fileName: 'reel.mp4', mediaType: 'video', mimeType: 'video/mp4', sizeBytes: 8_000_000, createdAt: '2026-11-05T09:00:00.0000000+00:00' });
const brief = asset('d1', { workspaceId: 'w1', fileName: 'brief.pdf', mediaType: 'document', mimeType: 'application/pdf', createdAt: '2026-11-04T09:00:00.0000000+00:00' });
const elsewhere = asset('x1', { workspaceId: 'another-workspace', fileName: 'other.png' });

function library(extra: ContentServerOptions = {}): ContentServerOptions {
  return { permissions: contentWithMediaPermissions, assets: [beach, sunset, reel, brief, elsewhere], ...extra };
}

function open(path = '/content-studio/new', options: ContentServerOptions = library(), overrides = {}) {
  const server = contentServer(options, overrides);
  return renderApp(path, server.routes).then((view) => ({ ...view, server }));
}

const panel = () => within(screen.getByRole('region', { name: 'Add Media' }));
const attachedNames = () => within(screen.getByRole('list', { name: 'Files on this post' })).getAllByRole('listitem').map((item) => within(item).getAllByText(/\.(png|mp4|pdf)$/)[0]?.textContent);
const dialog = (name = 'Choose from the Media Library') => screen.findByRole('dialog', { name });
const isDisabled = (element: HTMLElement) => element.matches(':disabled');

async function addFromLibrary(...names: string[]) {
  fireEvent.click(panel().getByRole('button', { name: 'Media Library' }));
  const picker = await dialog();
  for (const name of names) {
    fireEvent.click(await within(picker).findByRole('button', { name }));
  }
  fireEvent.click(within(picker).getByRole('button', { name: new RegExp(`^Add ${names.length === 1 ? '1 file' : `${names.length} files`}$`) }));
  await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
}

const savePost = async () => {
  fireEvent.click(within(screen.getByRole('group', { name: 'Post actions' })).getByRole('button', { name: 'Save Draft' }));
};

describe('Add Media on the Create page', () => {
  it('is live for someone who may look into the library: Upload and Media Library on, AI Generate still off', async () => {
    await open();
    await heading('Create New Post');

    expect(isDisabled(panel().getByRole('button', { name: 'Upload' }))).toBe(false);
    expect(isDisabled(panel().getByRole('button', { name: 'Media Library' }))).toBe(false);
    expect(isDisabled(panel().getByRole('button', { name: 'AI Generate' }))).toBe(true);
    expect(panel().getByText('Add images or videos, up to 10.')).toBeTruthy();
  });

  it('is off, and says so, where there is no Media Library', async () => {
    await open('/content-studio/new', library({ capabilities: { media: false, ai: false, campaigns: false, publishing: 'fake' } }));
    await heading('Create New Post');

    expect(isDisabled(panel().getByRole('button', { name: 'Media Library' }))).toBe(true);
    expect(isDisabled(panel().getByRole('button', { name: 'Upload' }))).toBe(true);
    expect(panel().getByText('The Media Library is not available.')).toBeTruthy();
  });

  it('is off, and says why, for a role that cannot look into the library', async () => {
    await open('/content-studio/new', library({ permissions: allContentPermissions }));
    await heading('Create New Post');

    expect(isDisabled(panel().getByRole('button', { name: 'Media Library' }))).toBe(true);
    expect(isDisabled(panel().getByRole('button', { name: 'Upload' }))).toBe(true);
    expect(panel().getByText('Your role cannot use the Media Library, so files cannot be added.')).toBeTruthy();
  });

  it('lets a role that may only look choose files but not upload new ones', async () => {
    await open('/content-studio/new', library({ permissions: [...allContentPermissions, PERMISSIONS.mediaLibraryView] }));
    await heading('Create New Post');

    expect(isDisabled(panel().getByRole('button', { name: 'Media Library' }))).toBe(false);
    expect(isDisabled(panel().getByRole('button', { name: 'Upload' }))).toBe(true);
    expect(panel().getByText(/Your role can choose files but not upload new ones/)).toBeTruthy();
  });

  describe('the library picker', () => {
    it('shows the images of the post\'s workspace, with the counts of each kind, and nothing from another workspace', async () => {
      await open();
      await heading('Create New Post');

      fireEvent.click(panel().getByRole('button', { name: 'Media Library' }));
      const picker = await dialog();

      expect(await within(picker).findByRole('button', { name: 'beach.png' })).toBeTruthy();
      expect(within(picker).getByRole('button', { name: 'sunset.png' })).toBeTruthy();
      expect(within(picker).queryByRole('button', { name: 'other.png' })).toBeNull();
      expect(within(picker).queryByRole('button', { name: 'reel.mp4' })).toBeNull();
      expect(within(picker).getByRole('button', { name: /^Images \(2\)$/ })).toBeTruthy();
      expect(within(picker).getByRole('button', { name: /^Videos \(1\)$/ })).toBeTruthy();
    });

    it('does not offer documents or sounds: a post carries only images and videos', async () => {
      await open();
      await heading('Create New Post');

      fireEvent.click(panel().getByRole('button', { name: 'Media Library' }));
      const picker = await dialog();
      await within(picker).findByRole('button', { name: 'beach.png' });

      expect(within(picker).queryByRole('button', { name: 'brief.pdf' })).toBeNull();
      expect(within(picker).queryByRole('button', { name: /Documents/ })).toBeNull();
    });

    it('switches to videos', async () => {
      await open();
      await heading('Create New Post');

      fireEvent.click(panel().getByRole('button', { name: 'Media Library' }));
      const picker = await dialog();
      fireEvent.click(within(picker).getByRole('button', { name: /^Videos/ }));

      expect(await within(picker).findByRole('button', { name: 'reel.mp4' })).toBeTruthy();
      await waitFor(() => expect(within(picker).queryByRole('button', { name: 'beach.png' })).toBeNull());
      expect(within(picker).getByRole('button', { name: /^Videos/ }).getAttribute('aria-pressed')).toBe('true');
    });

    it('searches, after the typing pauses', async () => {
      const { server } = await open();
      await heading('Create New Post');

      fireEvent.click(panel().getByRole('button', { name: 'Media Library' }));
      const picker = await dialog();
      await within(picker).findByRole('button', { name: 'beach.png' });
      fireEvent.change(within(picker).getByRole('searchbox', { name: 'Search files' }), { target: { value: 'sun' } });

      await waitFor(() => expect(within(picker).queryByRole('button', { name: 'beach.png' })).toBeNull());
      expect(within(picker).getByRole('button', { name: 'sunset.png' })).toBeTruthy();
      expect(server.library?.requests.some((line) => line.includes('q=sun'))).toBe(true);
    });

    it('says so when nothing matches, and when the library has none', async () => {
      await open();
      await heading('Create New Post');

      fireEvent.click(panel().getByRole('button', { name: 'Media Library' }));
      const picker = await dialog();
      await within(picker).findByRole('button', { name: 'beach.png' });
      fireEvent.change(within(picker).getByRole('searchbox', { name: 'Search files' }), { target: { value: 'zebra' } });

      expect(await within(picker).findByText('No file matches what you chose.')).toBeTruthy();
    });

    it('says the workspace has no files yet', async () => {
      await open('/content-studio/new', library({ assets: [] }));
      await heading('Create New Post');

      fireEvent.click(panel().getByRole('button', { name: 'Media Library' }));

      expect(await within(await dialog()).findByText(/There are no images in this workspace's library yet/)).toBeTruthy();
    });

    it('narrows to a folder', async () => {
      await open(
        '/content-studio/new',
        library({ assets: [asset('a1', { workspaceId: 'w1', fileName: 'beach.png', folderIds: ['f1'] }), sunset], folders: [{ ...folder('f1', 'Beach trip') }] }),
      );
      await heading('Create New Post');

      fireEvent.click(panel().getByRole('button', { name: 'Media Library' }));
      const picker = await dialog();
      await within(picker).findByRole('button', { name: 'sunset.png' });
      fireEvent.change(within(picker).getByLabelText('Folder'), { target: { value: 'f1' } });

      await waitFor(() => expect(within(picker).queryByRole('button', { name: 'sunset.png' })).toBeNull());
      expect(within(picker).getByRole('button', { name: 'beach.png' })).toBeTruthy();
    });

    it('shows the error and tries again', async () => {
      let failing = true;
      const flaky = contentServer(library());
      const real = flaky.library!.routes['GET /v1/media/assets?workspaceId=w1&type=image&page=1&pageSize=12'];
      const server = contentServer(library(), {
        'GET /v1/media/assets?workspaceId=w1&type=image&page=1&pageSize=12': (call) => (failing ? jsonResponse(500, { code: 'server_error' }) : (real as (c: typeof call) => Response)(call)),
      });
      await renderApp('/content-studio/new', server.routes);
      await heading('Create New Post');

      fireEvent.click(panel().getByRole('button', { name: 'Media Library' }));
      const picker = await dialog();
      expect(await within(picker).findByRole('alert')).toBeTruthy();

      failing = false;
      fireEvent.click(within(picker).getByRole('button', { name: 'Try again' }));
      expect(await within(picker).findByRole('button', { name: 'beach.png' })).toBeTruthy();
    });

    it('closes with Cancel or Escape and adds nothing', async () => {
      await open();
      await heading('Create New Post');

      fireEvent.click(panel().getByRole('button', { name: 'Media Library' }));
      const picker = await dialog();
      fireEvent.click(await within(picker).findByRole('button', { name: 'beach.png' }));
      fireEvent.click(within(picker).getByRole('button', { name: 'Cancel' }));

      expect(screen.queryByRole('dialog')).toBeNull();
      expect(screen.queryByRole('list', { name: 'Files on this post' })).toBeNull();

      fireEvent.click(panel().getByRole('button', { name: 'Media Library' }));
      await dialog();
      fireEvent.keyDown(document, { key: 'Escape' });
      await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
    });

    it('cannot add until something is ticked', async () => {
      await open();
      await heading('Create New Post');

      fireEvent.click(panel().getByRole('button', { name: 'Media Library' }));
      const picker = await dialog();
      await within(picker).findByRole('button', { name: 'beach.png' });

      expect(isDisabled(within(picker).getByRole('button', { name: 'Add' }))).toBe(true);
      expect(within(picker).getByText('Nothing ticked yet.')).toBeTruthy();
    });
  });

  describe('files on the post', () => {
    it('are added in the order they were ticked, and shown with their name, kind and size', async () => {
      await open();
      await heading('Create New Post');

      await addFromLibrary('sunset.png', 'beach.png');

      expect(attachedNames()).toEqual(['sunset.png', 'beach.png']);
      const items = within(screen.getByRole('list', { name: 'Files on this post' })).getAllByRole('listitem');
      expect(within(items[1]!).getByText(/Image · 1\.9 MB · 1600 × 900/)).toBeTruthy();
      expect(panel().getByText('2 of 10 files.')).toBeTruthy();
    });

    it('can be taken off, and moved earlier or later (buttons, so no dragging is needed)', async () => {
      await open();
      await heading('Create New Post');
      await addFromLibrary('beach.png', 'sunset.png');

      fireEvent.click(panel().getByRole('button', { name: 'Move sunset.png earlier' }));
      expect(attachedNames()).toEqual(['sunset.png', 'beach.png']);
      // The first cannot move earlier and the last cannot move later.
      expect(isDisabled(panel().getByRole('button', { name: 'Move sunset.png earlier' }))).toBe(true);
      expect(isDisabled(panel().getByRole('button', { name: 'Move beach.png later' }))).toBe(true);

      fireEvent.click(panel().getByRole('button', { name: 'Move sunset.png later' }));
      expect(attachedNames()).toEqual(['beach.png', 'sunset.png']);

      fireEvent.click(panel().getByRole('button', { name: 'Remove beach.png from the post' }));
      expect(attachedNames()).toEqual(['sunset.png']);
      fireEvent.click(panel().getByRole('button', { name: 'Remove sunset.png from the post' }));
      expect(screen.queryByRole('list', { name: 'Files on this post' })).toBeNull();
    });

    it('show a file already on the post as added, and cannot be ticked twice', async () => {
      await open();
      await heading('Create New Post');
      await addFromLibrary('beach.png');

      fireEvent.click(panel().getByRole('button', { name: 'Media Library' }));
      const picker = await dialog();

      const added = await within(picker).findByRole('button', { name: 'beach.png (already on the post)' });
      expect(isDisabled(added)).toBe(true);
      expect(within(picker).getByText('Added')).toBeTruthy();
      expect(isDisabled(within(picker).getByRole('button', { name: 'sunset.png' }))).toBe(false);
    });

    it('stop at ten: the picker offers only the free places, and a full post cannot add more', async () => {
      const nine: PostMediaItem[] = Array.from({ length: 9 }, (_, i) => ({
        assetId: `held-${i}`, role: 'slide', displayOrder: i, caption: '', fileName: `held-${i}.png`, mediaType: 'image', mimeType: 'image/png', sizeBytes: 1000, width: null, height: null,
      }));
      await open('/content-studio/p1', library({ posts: [post('p1', { contentType: 'carousel', media: nine })] }));
      await heading('Edit Post');

      expect(panel().getByText('9 of 10 files.')).toBeTruthy();
      fireEvent.click(panel().getByRole('button', { name: 'Media Library' }));
      const picker = await dialog();
      expect(within(picker).getByText('Tick up to 1 file to add to the post.')).toBeTruthy();

      fireEvent.click(await within(picker).findByRole('button', { name: 'beach.png' }));
      // The place is taken: the others cannot be ticked, but the ticked one can be un-ticked.
      expect(isDisabled(within(picker).getByRole('button', { name: 'sunset.png' }))).toBe(true);
      expect(isDisabled(within(picker).getByRole('button', { name: 'beach.png' }))).toBe(false);
      fireEvent.click(within(picker).getByRole('button', { name: 'Add 1 file' }));
      await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());

      expect(panel().getByText('A post can hold 10 files, and this one has them all.')).toBeTruthy();
      expect(isDisabled(panel().getByRole('button', { name: 'Media Library' }))).toBe(true);
      expect(isDisabled(panel().getByRole('button', { name: 'Upload' }))).toBe(true);
    });
  });

  describe('the preview and the review', () => {
    it('show the first file, and how many there are for a carousel', async () => {
      const { container } = await open('/content-studio/new?type=carousel');
      await heading('Create New Post');
      await addFromLibrary('beach.png', 'sunset.png');

      const media = container.querySelector('.preview-media');
      expect(media).not.toBeNull();
      expect(media?.querySelector('img')?.getAttribute('src')).toBe('/v1/media/assets/a1/file');
      expect(within(media as HTMLElement).getByText('1 / 2')).toBeTruthy();
    });

    it('show a post that is only a file, and the file alone does not say "your post will appear here"', async () => {
      const { container } = await open();
      await heading('Create New Post');
      await addFromLibrary('beach.png');

      expect(container.querySelector('.preview-media img')).not.toBeNull();
      expect(screen.queryByText('Your post will appear here as you write it.')).toBeNull();
    });

    it('list the files in the review', async () => {
      await open();
      await heading('Create New Post');
      await addFromLibrary('beach.png', 'sunset.png');

      fireEvent.click(within(screen.getByRole('navigation', { name: 'Steps' })).getByRole('button', { name: /Schedule & Publish/ }));

      expect(await screen.findByText('2 files: beach.png, sunset.png')).toBeTruthy();
    });

    it('say None in the review when there are no files', async () => {
      await open();
      await heading('Create New Post');

      fireEvent.click(within(screen.getByRole('navigation', { name: 'Steps' })).getByRole('button', { name: /Schedule & Publish/ }));

      const media = (await screen.findByText('Media')).nextElementSibling;
      expect(media?.textContent).toBe('None');
    });
  });

  describe('saving', () => {
    it('sends the files in order on a new post, then shows them as the server answered', async () => {
      const { server } = await open('/content-studio/new?type=blog_post');
      await heading('Create New Post');
      fireEvent.change(await screen.findByRole('textbox', { name: 'Post Editor' }), { target: { value: 'Hello' } });
      await addFromLibrary('sunset.png', 'beach.png');

      await savePost();

      expect(await screen.findByText('Draft saved.')).toBeTruthy();
      await waitFor(() => expect(location()).toBe('/content-studio/new-1'));
      const sent = server.bodies.find((entry) => entry.route === 'POST /v1/content/posts')?.body as { media: unknown };
      expect(sent.media).toEqual([
        { assetId: 'a2', caption: '' },
        { assetId: 'a1', caption: '' },
      ]);
      // A blog post's first picture is its cover, which only the server decides; the page shows what came back.
      expect(server.posts[0]?.media.map((m) => [m.assetId, m.role])).toEqual([['a2', 'cover'], ['a1', 'attachment']]);
      expect(attachedNames()).toEqual(['sunset.png', 'beach.png']);
    });

    it('the library then counts the post as using each file', async () => {
      const { server } = await open();
      await heading('Create New Post');
      fireEvent.change(await screen.findByRole('textbox', { name: 'Post Editor' }), { target: { value: 'Hello' } });
      await addFromLibrary('beach.png');

      await savePost();
      await screen.findByText('Draft saved.');

      expect(server.library?.assets.find((a) => a.id === 'a1')?.usageCount).toBe(1);
      expect(server.library?.assets.find((a) => a.id === 'a2')?.usageCount).toBe(0);
    });

    it('a post with files is not unsaved when it was just opened (the preview does not count as an edit)', async () => {
      const files: PostMediaItem[] = [{ assetId: 'a1', role: 'attachment', displayOrder: 0, caption: '', fileName: 'beach.png', mediaType: 'image', mimeType: 'image/png', sizeBytes: 2_000_000, width: 1600, height: 900 }];
      await open('/content-studio/p1', library({ posts: [post('p1', { media: files })] }));
      await heading('Edit Post');

      const closing = new Event('beforeunload', { cancelable: true });
      window.dispatchEvent(closing);

      expect(closing.defaultPrevented).toBe(false);
      expect(attachedNames()).toEqual(['beach.png']);
    });

    it('changing the files makes the post unsaved, and an edit sends the whole list again with the version it loaded', async () => {
      const files: PostMediaItem[] = [{ assetId: 'a1', role: 'attachment', displayOrder: 0, caption: 'Kept caption', fileName: 'beach.png', mediaType: 'image', mimeType: 'image/png', sizeBytes: 2_000_000, width: 1600, height: 900 }];
      const stored = post('p1', { media: files });
      // The fake server moves the version on when it saves, so what the page loaded is read now.
      const loadedVersion = stored.updatedAt;
      const { server } = await open('/content-studio/p1', library({ posts: [stored] }));
      await heading('Edit Post');

      await addFromLibrary('sunset.png');
      const closing = new Event('beforeunload', { cancelable: true });
      window.dispatchEvent(closing);
      expect(closing.defaultPrevented).toBe(true);

      await savePost();
      await screen.findByText('Draft saved.');
      const put = server.bodies.find((entry) => entry.route === 'PUT /v1/content/posts/p1')?.body as { media: unknown; expectedUpdatedAt: string };
      expect(put.media).toEqual([
        { assetId: 'a1', caption: 'Kept caption' },
        { assetId: 'a2', caption: '' },
      ]);
      expect(put.expectedUpdatedAt).toBe(loadedVersion);
    });

    it('taking every file off sends an empty list, which is how the server takes them off the post', async () => {
      const files: PostMediaItem[] = [{ assetId: 'a1', role: 'attachment', displayOrder: 0, caption: '', fileName: 'beach.png', mediaType: 'image', mimeType: 'image/png', sizeBytes: 2_000_000, width: null, height: null }];
      const { server } = await open('/content-studio/p1', library({ posts: [post('p1', { media: files })] }));
      await heading('Edit Post');

      fireEvent.click(panel().getByRole('button', { name: 'Remove beach.png from the post' }));
      await savePost();
      await screen.findByText('Draft saved.');

      expect((server.bodies.find((entry) => entry.route === 'PUT /v1/content/posts/p1')?.body as { media: unknown }).media).toEqual([]);
      expect(server.library?.assets.find((a) => a.id === 'a1')?.usageCount).toBe(0);
    });

    it('says what the server refused, under the files, and keeps them on the page', async () => {
      const { server } = await open();
      await heading('Create New Post');
      fireEvent.change(await screen.findByRole('textbox', { name: 'Post Editor' }), { target: { value: 'Hello' } });
      await addFromLibrary('beach.png');
      // Someone deleted the file from the library after it was chosen.
      server.library!.assets.splice(server.library!.assets.findIndex((a) => a.id === 'a1'), 1);

      await savePost();

      const message = await within(screen.getByRole('region', { name: 'Add Media' })).findByText('A file you chose is no longer in the Media Library.', { selector: '.form-error' });
      expect(message.getAttribute('role')).toBe('alert');
      expect(attachedNames()).toEqual(['beach.png']);
      expect(location()).toBe('/content-studio/new');
    });

    it('is refused for a file of another workspace, with the reason', async () => {
      // Not offered by the picker, but a request can name one.
      const { server } = await open('/content-studio/new', library(), {});
      await heading('Create New Post');
      fireEvent.change(await screen.findByRole('textbox', { name: 'Post Editor' }), { target: { value: 'Hello' } });
      await addFromLibrary('beach.png');
      // The file moved to another workspace after it was chosen.
      server.library!.assets.find((a) => a.id === 'a1')!.workspaceId = 'another-workspace';

      await savePost();

      expect(await screen.findAllByText('Files must come from the same workspace as the post.')).not.toHaveLength(0);
    });
  });

  describe('Upload', () => {
    const png = (name = 'diagram.png') => new File([new Uint8Array(2048)], name, { type: 'image/png' });

    async function upload(file: File) {
      fireEvent.click(panel().getByRole('button', { name: 'Upload' }));
      const dialog = await screen.findByRole('dialog', { name: 'Upload Media' });
      fireEvent.change(within(dialog).getByLabelText('Choose files to upload'), { target: { files: [file] } });
      fireEvent.click(within(dialog).getByRole('button', { name: 'Upload 1 file' }));
      await within(dialog).findByText('1 file was uploaded.');
      return dialog;
    }

    it('adds the file to the library of the post\'s workspace and puts it straight on the post', async () => {
      const { server } = await open();
      await heading('Create New Post');

      const uploading = await upload(png());

      expect(server.library?.uploads).toEqual([{ fields: { workspaceId: ['w1'], folderId: [], tags: [] }, fileName: 'diagram.png' }]);
      expect(attachedNames()).toEqual(['diagram.png']);
      fireEvent.click(within(uploading).getByRole('button', { name: 'Done' }));
      await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
      expect(panel().getByText('1 of 10 files.')).toBeTruthy();
    });

    it('puts a PDF in the library but not on the post, and says so', async () => {
      await open();
      await heading('Create New Post');

      await upload(png('brief.pdf'));

      expect(screen.queryByRole('list', { name: 'Files on this post' })).toBeNull();
      expect(panel().getByText('A file was added to the library but not to the post: only images and videos can go on a post.')).toBeTruthy();
    });

    it('is not offered to a role that may not upload', async () => {
      await open('/content-studio/new', library({ permissions: [...allContentPermissions, PERMISSIONS.mediaLibraryView] }));
      await heading('Create New Post');

      expect(isDisabled(panel().getByRole('button', { name: 'Upload' }))).toBe(true);
    });
  });

  describe('a post that can no longer be edited', () => {
    it('shows its files, and nothing in the panel can be pressed', async () => {
      const files: PostMediaItem[] = [{ assetId: 'a1', role: 'attachment', displayOrder: 0, caption: '', fileName: 'beach.png', mediaType: 'image', mimeType: 'image/png', sizeBytes: 2_000_000, width: null, height: null }];
      await open('/content-studio/p1', library({ posts: [post('p1', { status: 'published', publishedAt: '2026-11-06T09:00:00.0000000+00:00', media: files })] }));
      await heading('Edit Post');

      expect(attachedNames()).toEqual(['beach.png']);
      expect(isDisabled(panel().getByRole('button', { name: 'Media Library' }))).toBe(true);
      expect(isDisabled(panel().getByRole('button', { name: 'Upload' }))).toBe(true);
      expect(isDisabled(panel().getByRole('button', { name: 'Remove beach.png from the post' }))).toBe(true);
    });
  });

  it('keeps a file the library no longer describes on the post, by its id', async () => {
    const orphan: PostMediaItem[] = [{ assetId: 'gone', role: 'attachment', displayOrder: 0, caption: '', fileName: '', mediaType: '', mimeType: '', sizeBytes: 0, width: null, height: null }];
    await open('/content-studio/p1', library({ posts: [post('p1', { media: orphan })] }));
    await heading('Edit Post');

    const item = within(screen.getByRole('list', { name: 'Files on this post' })).getByRole('listitem');
    expect(within(item).getByText('A file of the library')).toBeTruthy();
    expect(within(item).getByRole('button', { name: 'Remove the file from the post' })).toBeTruthy();
    // The two names carry no empty parts ("Image ·  · ") for a file about which nothing is known.
    expect(item.querySelector('.media-sub')?.textContent).toBe('');
  });
});
