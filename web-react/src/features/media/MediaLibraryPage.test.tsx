import { act, fireEvent, screen, waitFor, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { PERMISSIONS } from '@/access/permissions';
import { jsonResponse, type Handler } from '@/test/helpers';
import { allMediaPermissions, asset, folder, mediaServer, tag, type MediaServerOptions } from '@/test/media';
import { renderApp } from '@/test/renderApp';

const heading = (name: string | RegExp) => screen.findByRole('heading', { level: 1, name });
const location = () => screen.getByTestId('location').textContent;
const names = () => screen.getAllByRole('button', { name: /^View / }).map((button) => button.getAttribute('aria-label')!.replace(/^View /, ''));
const side = () => within(screen.getByRole('complementary', { name: 'Folders and tags' }));
const tabs = () => within(screen.getByRole('navigation', { name: 'Kind of media' }));
const filesLoaded = () => screen.findByRole('list', { name: 'Files' });

const viewer = [PERMISSIONS.dashboardView, PERMISSIONS.mediaLibraryView];
const uploader = [...viewer, PERMISSIONS.mediaLibraryCreate];

const sample = () => [
  asset('a1', {
    fileName: 'mountains.jpg',
    mimeType: 'image/jpeg',
    sizeBytes: 2_516_582,
    width: 4000,
    height: 3000,
    createdAt: '2026-11-08T09:00:00.0000000+00:00',
    tags: [tag('a1000000', 'travel')],
    folderIds: ['f1000000'],
  }),
  asset('a2', { fileName: 'travel-video.mp4', mediaType: 'video', mimeType: 'video/mp4', sizeBytes: 9_122_611, createdAt: '2026-11-07T09:00:00.0000000+00:00', folderIds: ['f2000000'], tags: [tag('a1000000', 'travel')] }),
  asset('a3', { fileName: 'brief.pdf', mediaType: 'document', mimeType: 'application/pdf', sizeBytes: 500_000, createdAt: '2026-11-06T09:00:00.0000000+00:00' }),
  asset('a4', { fileName: 'jingle.mp3', mediaType: 'other', mimeType: 'audio/mpeg', sizeBytes: 3_000_000, createdAt: '2026-11-05T09:00:00.0000000+00:00', usageCount: 2 }),
];

const sampleFolders = [folder('f1000000', 'Social Posts'), folder('f2000000', 'Videos')];

function open(options: MediaServerOptions = {}, path = '/media-library', overrides = {}) {
  const server = mediaServer({ assets: sample(), folders: sampleFolders, ...options }, overrides);
  return renderApp(path, server.routes).then((view) => ({ ...view, server }));
}

const select = (label: string | RegExp, value: string) => fireEvent.change(screen.getByLabelText(label), { target: { value } });
const click = (name: string | RegExp, role: 'button' | 'link' | 'menuitem' = 'button') => fireEvent.click(screen.getByRole(role, { name }));
const menuOf = (file: string) => fireEvent.click(screen.getByRole('button', { name: `Actions for ${file}` }));

describe('the Media Library list', () => {
  it('lists the files of the workspace, newest first, with their size and date, and the count of each kind in its tab', async () => {
    await open();

    expect(await heading('Media Library')).toBeTruthy();
    await filesLoaded();
    expect(names()).toEqual(['mountains.jpg', 'travel-video.mp4', 'brief.pdf', 'jingle.mp3']);
    expect(screen.getByText(/2\.4 MB · Nov 8, 2026/)).toBeTruthy();
    expect(tabs().getByRole('link', { name: /All Media\s*\(4\)/ })).toBeTruthy();
    expect(tabs().getByRole('link', { name: /Images\s*\(1\)/ })).toBeTruthy();
    expect(tabs().getByRole('link', { name: /Videos\s*\(1\)/ })).toBeTruthy();
    expect(tabs().getByRole('link', { name: /Documents\s*\(1\)/ })).toBeTruthy();
    expect(tabs().getByRole('link', { name: /Others\s*\(1\)/ })).toBeTruthy();
    expect(document.title).toBe('Media Library · Platform');
  });

  it('shows a picture as the picture itself, a video as a video, and a document as a drawing with its extension', async () => {
    await open();
    await filesLoaded();

    const img = screen.getByRole('button', { name: 'View mountains.jpg' }).querySelector('img');
    expect(img?.getAttribute('src')).toBe('/v1/media/assets/a1/file');
    expect(img?.getAttribute('loading')).toBe('lazy');
    expect(screen.getByRole('button', { name: 'View travel-video.mp4' }).querySelector('video')?.getAttribute('src')).toBe('/v1/media/assets/a2/file#t=0.1');
    const document_ = screen.getByRole('button', { name: 'View brief.pdf' });
    expect(document_.querySelector('img, video')).toBeNull();
    expect(document_.textContent).toBe('PDF');
  });

  it('keeps the counts of the tabs while one kind is open, and puts the kind in the address', async () => {
    await open();
    await filesLoaded();

    fireEvent.click(tabs().getByRole('link', { name: /Videos/ }));

    await waitFor(() => expect(location()).toBe('/media-library?type=video'));
    await waitFor(() => expect(names()).toEqual(['travel-video.mp4']));
    expect(tabs().getByRole('link', { name: /All Media\s*\(4\)/ })).toBeTruthy();
    expect(tabs().getByRole('link', { name: /Videos/ }).getAttribute('aria-current')).toBe('page');
  });

  it('reads the choices from the address, and ignores what the page does not know', async () => {
    const { server } = await open({}, '/media-library?type=zzz&folder=../../etc&size=huge&sort=random&view=table&page=-4');
    await filesLoaded();

    expect(server.requests.filter((line) => line.startsWith('GET /v1/media/assets?'))).toEqual(['GET /v1/media/assets?workspaceId=b1000000&page=1&pageSize=12']);
    expect(names()).toHaveLength(4);
  });

  it('narrows by size and by date through the address, and Reset undoes that but keeps the kind', async () => {
    await open({}, '/media-library?type=image');
    await filesLoaded();

    select('Size', 'large');
    await waitFor(() => expect(location()).toBe('/media-library?type=image&size=large'));
    await screen.findByText('No files match what you chose.');

    click('Reset');
    await waitFor(() => expect(location()).toBe('/media-library?type=image'));
    await waitFor(() => expect(names()).toEqual(['mountains.jpg']));
  });

  it('searches after the typing pauses, matching the name of a file or of a tag', async () => {
    const { server } = await open();
    await filesLoaded();

    fireEvent.change(screen.getByRole('searchbox', { name: 'Search media' }), { target: { value: 'jingle' } });
    expect(server.requests.some((line) => line.includes('q=jingle'))).toBe(false);

    await waitFor(() => expect(location()).toBe('/media-library?q=jingle'));
    await waitFor(() => expect(names()).toEqual(['jingle.mp3']));

    fireEvent.change(screen.getByRole('searchbox', { name: 'Search media' }), { target: { value: 'TRAVEL' } });
    await waitFor(() => expect(names()).toEqual(['mountains.jpg', 'travel-video.mp4']));
  });

  it('sorts, and writes the order in the address', async () => {
    await open();
    await filesLoaded();

    select('Sort by', 'name');

    await waitFor(() => expect(location()).toBe('/media-library?sort=name'));
    await waitFor(() => expect(names()).toEqual(['brief.pdf', 'jingle.mp3', 'mountains.jpg', 'travel-video.mp4']));
  });

  it('switches to a list of rows that says more about each file, and keeps the choice in the address', async () => {
    await open();
    await filesLoaded();

    click('List view');

    await waitFor(() => expect(location()).toBe('/media-library?view=list'));
    expect(screen.getByText(/Image · 2\.4 MB · 4000 × 3000 · Nov 8, 2026/)).toBeTruthy();
    expect(screen.getByRole('button', { name: 'List view' }).getAttribute('aria-pressed')).toBe('true');
    expect(screen.getByRole('button', { name: 'Grid view' }).getAttribute('aria-pressed')).toBe('false');
    click('Grid view');
    await waitFor(() => expect(location()).toBe('/media-library'));
  });

  it('pages through a long library', async () => {
    const many = Array.from({ length: 30 }, (_, i) => asset(`m${String(i).padStart(2, '0')}`, { fileName: `photo-${String(i).padStart(2, '0')}.png`, createdAt: `2026-10-${String(30 - i).padStart(2, '0')}T09:00:00.0000000+00:00` }));
    await open({ assets: many });
    await filesLoaded();

    expect(names()).toHaveLength(12);
    expect(screen.getByText(/1–12 of 30/)).toBeTruthy();
    click('Next');

    await waitFor(() => expect(location()).toBe('/media-library?page=2'));
    await waitFor(() => expect(screen.getByText(/13–24 of 30/)).toBeTruthy());
    expect(names()[0]).toBe('photo-12.png');
  });

  it('says there are no files yet, and points at Upload Media only for someone who can add one', async () => {
    await open({ assets: [], permissions: uploader });
    expect(await screen.findByText('There are no files yet.')).toBeTruthy();
    expect(screen.getByText(/Use “Upload Media”/)).toBeTruthy();
  });

  it('says there are no files yet without a hint to a reader', async () => {
    await open({ assets: [], permissions: viewer });
    expect(await screen.findByText('There are no files yet.')).toBeTruthy();
    expect(screen.queryByText(/Use “Upload Media”/)).toBeNull();
  });

  it('shows an error with Try again, and tries again', async () => {
    let calls = 0;
    await open({}, '/media-library', {
      'GET /v1/media/assets?workspaceId=b1000000&page=1&pageSize=12': () => {
        calls += 1;
        return calls === 1 ? jsonResponse(500, { detail: 'boom' }) : jsonResponse(200, { items: [asset('x1', { fileName: 'recovered.png' })], page: 1, pageSize: 12, totalCount: 1, totalPages: 1, typeCounts: { all: 1, image: 1, video: 0, document: 0, other: 0 } });
      },
    });

    expect(await screen.findByText('The media could not be loaded.')).toBeTruthy();
    click('Try again');

    await waitFor(() => expect(names()).toEqual(['recovered.png']));
  });
});

describe('folders and tags', () => {
  it('lists All Media, each folder and Uncategorized with how many files each holds', async () => {
    await open();
    await filesLoaded();

    const panel = side();
    expect((await panel.findByRole('link', { name: /All Media/ })).textContent).toContain('4');
    expect(panel.getByRole('link', { name: /Social Posts/ }).textContent).toContain('1');
    expect(panel.getByRole('link', { name: /Videos/ }).textContent).toContain('1');
    expect(panel.getByRole('link', { name: /Uncategorized/ }).textContent).toContain('2');
    expect(panel.getByRole('link', { name: /All Media/ }).getAttribute('aria-current')).toBe('page');
  });

  it('opens a folder through the address, and Uncategorized as the files that are in none', async () => {
    const { server } = await open();
    await filesLoaded();

    fireEvent.click(await side().findByRole('link', { name: /Videos/ }));
    await waitFor(() => expect(location()).toBe('/media-library?folder=f2000000'));
    await waitFor(() => expect(names()).toEqual(['travel-video.mp4']));
    expect(side().getByRole('link', { name: /Videos/ }).getAttribute('aria-current')).toBe('page');

    fireEvent.click(side().getByRole('link', { name: /Uncategorized/ }));
    await waitFor(() => expect(location()).toBe('/media-library?folder=none'));
    await waitFor(() => expect(names()).toEqual(['brief.pdf', 'jingle.mp3']));
    expect(server.requests.some((line) => line.includes('uncategorized=true'))).toBe(true);
  });

  it('filters by a tag, and the same tag again takes the filter off', async () => {
    await open();
    await filesLoaded();

    fireEvent.click(await side().findByRole('link', { name: /#travel/ }));
    await waitFor(() => expect(location()).toBe('/media-library?tag=a1000000'));
    await waitFor(() => expect(names()).toEqual(['mountains.jpg', 'travel-video.mp4']));

    fireEvent.click(side().getByRole('link', { name: /#travel/ }));
    await waitFor(() => expect(location()).toBe('/media-library'));
  });

  it('keeps the tag in the filter row, so it can be chosen from a list too', async () => {
    await open();
    await filesLoaded();
    await side().findByRole('link', { name: /#travel/ });

    select('Tag', 'a1000000');

    await waitFor(() => expect(location()).toBe('/media-library?tag=a1000000'));
  });

  it('offers the folder buttons only to those who may make and delete folders', async () => {
    await open({ permissions: viewer });
    await filesLoaded();
    await side().findByRole('link', { name: /Videos/ });
    expect(screen.queryByRole('button', { name: 'Create folder' })).toBeNull();
    expect(screen.queryByRole('button', { name: /^Delete folder/ })).toBeNull();
  });

  it('makes a folder, and shows it', async () => {
    const { server } = await open({ permissions: uploader });
    await filesLoaded();

    click('Create Folder');
    fireEvent.change(screen.getByLabelText('Folder name'), { target: { value: '  Blog Images ' } });
    fireEvent.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Create folder' }));

    expect(await screen.findByText('Created the folder “Blog Images”.')).toBeTruthy();
    expect(server.bodies.find((entry) => entry.route === 'POST /v1/media/folders')?.body).toEqual({ workspaceId: 'b1000000', name: 'Blog Images' });
    expect(await side().findByRole('link', { name: /Blog Images/ })).toBeTruthy();
    expect(screen.queryByRole('dialog')).toBeNull();
  });

  it('answers a name the workspace already has under the field, and keeps the dialog open', async () => {
    await open({ permissions: uploader });
    await filesLoaded();

    click('Create Folder');
    fireEvent.change(screen.getByLabelText('Folder name'), { target: { value: 'videos' } });
    fireEvent.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Create folder' }));

    expect((await screen.findByRole('alert')).textContent).toBe('There is already a folder with that name.');
    expect(screen.getByRole('dialog')).toBeTruthy();
  });

  it('does not send an empty folder name', async () => {
    const { server } = await open({ permissions: uploader });
    await filesLoaded();

    click('Create Folder');
    fireEvent.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Create folder' }));

    expect((await screen.findByRole('alert')).textContent).toBe('A folder name is 1 to 100 characters.');
    expect(server.requests.some((line) => line.startsWith('POST /v1/media/folders'))).toBe(false);
  });

  it('deletes a folder after asking, leaves its files, and goes back to All Media if it was open', async () => {
    const { server } = await open({ permissions: allMediaPermissions }, '/media-library?folder=f2000000');
    await filesLoaded();

    fireEvent.click(await screen.findByRole('button', { name: 'Delete folder Videos' }));
    const dialog = screen.getByRole('dialog');
    expect(within(dialog).getByText(/The 1 file in it are not deleted/)).toBeTruthy();
    fireEvent.click(within(dialog).getByRole('button', { name: 'Delete folder' }));

    expect(await screen.findByText(/Deleted the folder “Videos”/)).toBeTruthy();
    expect(server.requests).toContain('DELETE /v1/media/folders/f2000000');
    await waitFor(() => expect(location()).toBe('/media-library'));
    await waitFor(() => expect(names()).toHaveLength(4));
    expect(screen.queryByRole('link', { name: /Videos\s*1/ })).toBeNull();
  });
});

describe('what a person may do', () => {
  it('gives a reader no way to add, organize or delete: no buttons, no tick boxes, only View details and Download in a menu', async () => {
    await open({ permissions: viewer });
    await filesLoaded();

    expect(screen.queryByRole('button', { name: 'Upload Media' })).toBeNull();
    expect(screen.queryByRole('button', { name: 'Create Folder' })).toBeNull();
    expect(screen.queryByRole('checkbox')).toBeNull();
    expect(screen.queryByRole('button', { name: 'Move' })).toBeNull();
    expect(screen.queryByRole('button', { name: 'Delete' })).toBeNull();

    menuOf('mountains.jpg');
    const items = within(screen.getByRole('menu')).getAllByRole('menuitem').map((item) => item.textContent);
    expect(items).toEqual(['View details', 'Download']);
  });

  it('gives someone who can add files Move and Copy but not Delete', async () => {
    await open({ permissions: uploader });
    await filesLoaded();

    expect(screen.getByRole('button', { name: 'Upload Media' })).toBeTruthy();
    expect(screen.getByRole('button', { name: 'Move' })).toBeTruthy();
    expect(screen.getByRole('button', { name: 'Copy' })).toBeTruthy();
    expect(screen.queryByRole('button', { name: 'Delete' })).toBeNull();

    menuOf('mountains.jpg');
    expect(within(screen.getByRole('menu')).getAllByRole('menuitem').map((item) => item.textContent)).toEqual([
      'View details',
      'Download',
      'Move to folder…',
      'Copy to folder…',
    ]);
  });

  it('offers Delete in the menu only for a file that nothing uses', async () => {
    await open({ permissions: allMediaPermissions });
    await filesLoaded();

    menuOf('brief.pdf');
    expect(within(screen.getByRole('menu')).getByRole('menuitem', { name: 'Delete' })).toBeTruthy();
    fireEvent.keyDown(document, { key: 'Escape' });

    menuOf('jingle.mp3');
    expect(within(screen.getByRole('menu')).queryByRole('menuitem', { name: 'Delete' })).toBeNull();
  });

  it('never mounts the page, or asks for any media, for someone without medialibrary.view', async () => {
    const server = mediaServer({ assets: sample(), permissions: [PERMISSIONS.dashboardView] });
    await renderApp('/media-library', server.routes);

    expect(await screen.findByRole('heading', { level: 1, name: 'You do not have access to this page' })).toBeTruthy();
    expect(server.requests).toEqual([]);
  });

  it('lists the four ways of adding media, and switches off the three that do not exist yet', async () => {
    await open({ permissions: uploader });
    await filesLoaded();

    click('Upload Media');
    const menu = screen.getByRole('menu', { name: 'Ways to add media' });
    expect((within(menu).getByRole('menuitem', { name: /Upload Files/ }) as HTMLButtonElement).disabled).toBe(false);
    for (const name of ['Upload from URL', 'Generate with AI', 'Record Video']) {
      const item = within(menu).getByRole('menuitem', { name: new RegExp(name) }) as HTMLButtonElement;
      expect(item.disabled).toBe(true);
      expect(item.textContent).toContain('Soon');
    }
  });
});

describe('looking at one file', () => {
  it('opens the details with what is known about the file, and a link to download it', async () => {
    await open({ permissions: viewer });
    await filesLoaded();

    fireEvent.click(screen.getByRole('button', { name: 'View mountains.jpg' }));

    const dialog = await screen.findByRole('dialog');
    expect(within(dialog).getByRole('heading', { name: 'mountains.jpg' })).toBeTruthy();
    expect(within(dialog).getByAltText('mountains.jpg').getAttribute('src')).toBe('/v1/media/assets/a1/file');
    const details = within(dialog).getByText('Kind').closest('dl')!;
    expect(details.textContent).toContain('Image');
    expect(details.textContent).toContain('2.4 MB');
    expect(details.textContent).toContain('4000 × 3000');
    expect(details.textContent).toContain('Social Posts');
    expect(details.textContent).toContain('#travel');
    expect(details.textContent).toContain('Not used yet');
    const download = within(dialog).getByRole('link', { name: /Download/ });
    expect(download.getAttribute('href')).toBe('/v1/media/assets/a1/file?download=true');
    expect(download.getAttribute('download')).toBe('mountains.jpg');
    expect(within(dialog).queryByRole('button', { name: 'Delete' })).toBeNull();

    click('Close');
    expect(screen.queryByRole('dialog')).toBeNull();
  });

  it('plays a video and a sound in the browser, and offers a document as a download', async () => {
    await open({ permissions: viewer });
    await filesLoaded();

    fireEvent.click(screen.getByRole('button', { name: 'View travel-video.mp4' }));
    expect((await screen.findByRole('dialog')).querySelector('video[controls]')?.getAttribute('src')).toBe('/v1/media/assets/a2/file');
    click('Close');

    fireEvent.click(screen.getByRole('button', { name: 'View jingle.mp3' }));
    expect((await screen.findByRole('dialog')).querySelector('audio[controls]')?.getAttribute('src')).toBe('/v1/media/assets/a4/file');
    click('Close');

    fireEvent.click(screen.getByRole('button', { name: 'View brief.pdf' }));
    expect((await screen.findByRole('dialog')).textContent).toContain('There is no preview for this kind of file');
  });

  it('says how many places use a file, and does not offer to delete it', async () => {
    await open({ permissions: allMediaPermissions });
    await filesLoaded();

    fireEvent.click(screen.getByRole('button', { name: 'View jingle.mp3' }));

    const dialog = await screen.findByRole('dialog');
    expect(dialog.textContent).toContain('2 places');
    expect(within(dialog).queryByRole('button', { name: 'Delete' })).toBeNull();
  });
});

describe('selecting files and changing them', () => {
  const tick = (file: string) => fireEvent.click(screen.getByRole('checkbox', { name: `Select ${file}` }));

  it('counts what is ticked, ticks the whole page, and starts again on another page', async () => {
    await open({ permissions: allMediaPermissions });
    await filesLoaded();
    expect(screen.getByText('0 selected')).toBeTruthy();
    expect((screen.getByRole('button', { name: 'Move' }) as HTMLButtonElement).disabled).toBe(true);

    tick('mountains.jpg');
    tick('brief.pdf');
    expect(screen.getByText('2 selected')).toBeTruthy();
    expect((screen.getByRole('button', { name: 'Move' }) as HTMLButtonElement).disabled).toBe(false);

    fireEvent.click(screen.getByRole('checkbox', { name: 'Select all files on this page' }));
    expect(screen.getByText('4 selected')).toBeTruthy();
    fireEvent.click(screen.getByRole('checkbox', { name: 'Select all files on this page' }));
    expect(screen.getByText('0 selected')).toBeTruthy();

    tick('mountains.jpg');
    fireEvent.click(tabs().getByRole('link', { name: /Videos/ }));
    await waitFor(() => expect(screen.getByText('0 selected')).toBeTruthy());
  });

  it('moves the ticked files to a folder', async () => {
    const { server } = await open({ permissions: uploader });
    await filesLoaded();

    tick('brief.pdf');
    tick('jingle.mp3');
    click('Move');
    const dialog = screen.getByRole('dialog');
    expect(within(dialog).getByRole('heading', { name: 'Move 2 files' })).toBeTruthy();
    fireEvent.click(within(dialog).getByRole('radio', { name: 'Videos' }));
    fireEvent.click(within(dialog).getByRole('button', { name: 'Move here' }));

    expect(await screen.findByText('Moved 2 files to Videos.')).toBeTruthy();
    expect(server.bodies.find((entry) => entry.route === 'POST /v1/media/assets/organize')?.body).toEqual({ assetIds: ['a3', 'a4'], folderId: 'f2000000', mode: 'move' });
    await waitFor(() => expect(screen.getByText('0 selected')).toBeTruthy());
    expect(server.assets.find((entry) => entry.id === 'a3')?.folderIds).toEqual(['f2000000']);
    expect(screen.queryByRole('dialog')).toBeNull();
  });

  it('can move files out of every folder', async () => {
    const { server } = await open({ permissions: uploader });
    await filesLoaded();

    menuOf('mountains.jpg');
    fireEvent.click(screen.getByRole('menuitem', { name: 'Move to folder…' }));
    const dialog = screen.getByRole('dialog');
    fireEvent.click(within(dialog).getByRole('radio', { name: 'No folder (Uncategorized)' }));
    fireEvent.click(within(dialog).getByRole('button', { name: 'Move here' }));

    expect(await screen.findByText('Moved 1 file to Uncategorized.')).toBeTruthy();
    expect(server.bodies.find((entry) => entry.route === 'POST /v1/media/assets/organize')?.body).toEqual({ assetIds: ['a1'], folderId: null, mode: 'move' });
    expect(server.assets.find((entry) => entry.id === 'a1')?.folderIds).toEqual([]);
  });

  it('copies a file into a folder and leaves it where it was', async () => {
    const { server } = await open({ permissions: uploader });
    await filesLoaded();

    menuOf('mountains.jpg');
    fireEvent.click(screen.getByRole('menuitem', { name: 'Copy to folder…' }));
    const dialog = screen.getByRole('dialog');
    expect(within(dialog).queryByRole('radio', { name: /Uncategorized/ })).toBeNull();
    fireEvent.click(within(dialog).getByRole('radio', { name: 'Videos' }));
    fireEvent.click(within(dialog).getByRole('button', { name: 'Copy here' }));

    expect(await screen.findByText('Copied 1 file to Videos.')).toBeTruthy();
    expect(server.assets.find((entry) => entry.id === 'a1')?.folderIds).toEqual(['f1000000', 'f2000000']);
  });

  it('cannot copy with no folder to copy to', async () => {
    await open({ permissions: uploader, folders: [] });
    await filesLoaded();

    menuOf('mountains.jpg');
    fireEvent.click(screen.getByRole('menuitem', { name: 'Copy to folder…' }));

    const dialog = screen.getByRole('dialog');
    expect(within(dialog).getByText('There are no folders yet. Create one first.')).toBeTruthy();
    expect((within(dialog).getByRole('button', { name: 'Copy here' }) as HTMLButtonElement).disabled).toBe(true);
  });

  it('shows what the server refused, and keeps the dialog open', async () => {
    await open({ permissions: uploader }, '/media-library', {
      'POST /v1/media/assets/organize': jsonResponse(409, { code: 'workspace_mismatch', detail: 'x' }),
    });
    await filesLoaded();

    menuOf('mountains.jpg');
    fireEvent.click(screen.getByRole('menuitem', { name: 'Move to folder…' }));
    fireEvent.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Move here' }));

    expect((await within(screen.getByRole('dialog')).findByRole('alert')).textContent).toBe('Files can only be put in a folder of their own workspace.');
  });

  it('deletes one file after asking, and shows the list without it', async () => {
    const { server } = await open({ permissions: allMediaPermissions });
    await filesLoaded();

    menuOf('brief.pdf');
    fireEvent.click(screen.getByRole('menuitem', { name: 'Delete' }));
    const dialog = screen.getByRole('dialog');
    expect(within(dialog).getByRole('heading', { name: 'Delete this file?' })).toBeTruthy();
    expect(within(dialog).getByText('brief.pdf')).toBeTruthy();
    expect(server.requests.some((line) => line.startsWith('DELETE'))).toBe(false);
    fireEvent.click(within(dialog).getByRole('button', { name: 'Delete' }));

    expect(await screen.findByText('Deleted 1 file.')).toBeTruthy();
    expect(server.requests).toContain('DELETE /v1/media/assets/a3');
    await waitFor(() => expect(names()).toEqual(['mountains.jpg', 'travel-video.mp4', 'jingle.mp3']));
  });

  it('does not delete when the question is answered No', async () => {
    const { server } = await open({ permissions: allMediaPermissions });
    await filesLoaded();

    menuOf('brief.pdf');
    fireEvent.click(screen.getByRole('menuitem', { name: 'Delete' }));
    fireEvent.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Cancel' }));

    expect(screen.queryByRole('dialog')).toBeNull();
    expect(server.requests.some((line) => line.startsWith('DELETE'))).toBe(false);
  });

  it('deletes the ticked files one by one, and says plainly which could not be deleted', async () => {
    const { server } = await open({ permissions: allMediaPermissions });
    await filesLoaded();

    tick('brief.pdf');
    tick('jingle.mp3');   // in use: the server refuses it
    click('Delete');
    expect(within(screen.getByRole('dialog')).getByRole('heading', { name: 'Delete 2 files?' })).toBeTruthy();
    fireEvent.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Delete' }));

    expect(await screen.findByText('Deleted 1 file.')).toBeTruthy();
    expect((await screen.findAllByRole('alert')).map((alert) => alert.textContent).join(' ')).toContain('1 file could not be deleted. This file is used by content, so it cannot be deleted.');
    expect(server.requests).toContain('DELETE /v1/media/assets/a3');
    expect(server.requests).toContain('DELETE /v1/media/assets/a4');
    expect(screen.queryByRole('dialog')).toBeNull();
    await waitFor(() => expect(names()).toEqual(['mountains.jpg', 'travel-video.mp4', 'jingle.mp3']));
  });

  it('deletes from the details of a file too', async () => {
    const { server } = await open({ permissions: allMediaPermissions });
    await filesLoaded();

    fireEvent.click(screen.getByRole('button', { name: 'View brief.pdf' }));
    fireEvent.click(within(await screen.findByRole('dialog')).getByRole('button', { name: 'Delete' }));
    fireEvent.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Delete' }));

    expect(await screen.findByText('Deleted 1 file.')).toBeTruthy();
    expect(server.assets.map((entry) => entry.id)).not.toContain('a3');
  });
});

describe('uploading', () => {
  const png = (name = 'photo.png', size = 100) => new File([new Uint8Array(size)], name, { type: 'image/png' });
  const input = () => screen.getByLabelText('Choose files to upload') as HTMLInputElement;
  const choose = (...files: File[]) => fireEvent.change(input(), { target: { files } });
  const openDialog = async (options: MediaServerOptions = {}, path = '/media-library', overrides: Record<string, Handler> = {}) => {
    const view = await open({ permissions: uploader, ...options }, path, overrides);
    await filesLoaded();
    click('Upload Media');
    fireEvent.click(screen.getByRole('menuitem', { name: /Upload Files/ }));
    return { ...view, dialog: () => screen.getByRole('dialog') };
  };
  const submit = () => fireEvent.click(within(screen.getByRole('dialog')).getByRole('button', { name: /^Upload (\d+|1) files?$/ }));

  it('sends each chosen file as a form with its workspace, folder and tags, and then shows the new files', async () => {
    const { server, dialog } = await openDialog({}, '/media-library?folder=f2000000');

    expect((within(dialog()).getByLabelText('Folder') as HTMLSelectElement).value).toBe('f2000000');
    choose(png('sunrise.png'), png('sunset.png'));
    fireEvent.change(within(dialog()).getByLabelText('Tags'), { target: { value: 'Summer, #beach, summer' } });
    expect(within(dialog()).getByRole('list', { name: 'Files to upload' }).textContent).toContain('sunrise.png');
    submit();

    expect(await within(dialog()).findByText('2 files were uploaded.')).toBeTruthy();
    expect(server.uploads).toEqual([
      { fields: { workspaceId: ['b1000000'], folderId: ['f2000000'], tags: ['Summer', 'beach'] }, fileName: 'sunrise.png' },
      { fields: { workspaceId: ['b1000000'], folderId: ['f2000000'], tags: ['Summer', 'beach'] }, fileName: 'sunset.png' },
    ]);
    expect(within(dialog()).getAllByText('Uploaded')).toHaveLength(2);
    click('Done');
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
    await waitFor(() => expect(side().getByRole('link', { name: /Videos/ }).textContent).toContain('3'));
  });

  it('sends no folder when none is chosen', async () => {
    const { server, dialog } = await openDialog();

    expect((within(dialog()).getByLabelText('Folder') as HTMLSelectElement).value).toBe('');
    choose(png());
    submit();

    await within(dialog()).findByText('1 file was uploaded.');
    expect(server.uploads[0]?.fields.folderId).toEqual([]);
    expect(server.uploads[0]?.fields.tags).toEqual([]);
  });

  it('marks a file that cannot be sent at once, and sends nothing for it', async () => {
    const { server, dialog } = await openDialog();

    choose(png('logo.svg'), png('huge.png', 6 * 1024 * 1024), png('empty.png', 0));

    const alerts = within(dialog()).getAllByRole('alert').map((alert) => alert.textContent);
    expect(alerts).toEqual([
      'This type of file is not supported. Use .png, .jpg, .jpeg, .gif, .webp, .mp4, .mov, .webm, .pdf, .mp3, .wav.',
      'This file is larger than 5 MB.',
      'This file is empty.',
    ]);
    expect((within(dialog()).getByRole('button', { name: 'Upload' }) as HTMLButtonElement).disabled).toBe(true);
    expect(server.uploads).toEqual([]);
  });

  it('sends the files that are fine and shows why the server refused another, without stopping the rest', async () => {
    const { server, dialog } = await openDialog();

    choose(png('refused.png'), png('fine.png'));
    submit();

    expect(await within(dialog()).findByText('1 file was uploaded.')).toBeTruthy();
    expect(server.uploads.map((upload) => upload.fileName)).toEqual(['refused.png', 'fine.png']);
    const list = within(dialog()).getByRole('list', { name: 'Files to upload' });
    expect(within(list).getByText(/This type of file is not supported\. Use PNG, JPEG/)).toBeTruthy();
    expect(within(list).getByText('Uploaded')).toBeTruthy();
    // The one that was refused can be taken off the list, or sent again.
    expect(within(list).getByRole('button', { name: 'Remove refused.png from the list' })).toBeTruthy();
    expect(within(dialog()).getByRole('button', { name: 'Upload 1 file' })).toBeTruthy();
  });

  it('takes a file off the list before it is sent', async () => {
    const { server, dialog } = await openDialog();

    choose(png('one.png'), png('two.png'));
    fireEvent.click(within(dialog()).getByRole('button', { name: 'Remove one.png from the list' }));
    submit();

    await within(dialog()).findByText('1 file was uploaded.');
    expect(server.uploads.map((upload) => upload.fileName)).toEqual(['two.png']);
  });

  it('does not send while the tags have a problem', async () => {
    const { server, dialog } = await openDialog();

    choose(png());
    fireEvent.change(within(dialog()).getByLabelText('Tags'), { target: { value: 'x'.repeat(60) } });

    expect(within(dialog()).getByText('A tag is at most 50 characters.')).toBeTruthy();
    expect((within(dialog()).getByRole('button', { name: 'Upload 1 file' }) as HTMLButtonElement).disabled).toBe(true);
    expect(server.uploads).toEqual([]);
  });

  it('accepts files dropped on the box, as an alternative to choosing them', async () => {
    const { server, dialog } = await openDialog();
    const box = within(dialog()).getByText('Drag files here, or').closest('.media-drop')!;

    fireEvent.dragOver(box);
    expect(box.getAttribute('data-over')).toBe('true');
    fireEvent.drop(box, { dataTransfer: { files: [png('dropped.png')] } });
    submit();

    await within(dialog()).findByText('1 file was uploaded.');
    expect(server.uploads.map((upload) => upload.fileName)).toEqual(['dropped.png']);
    expect(box.getAttribute('data-over')).toBeNull();
  });

  it('tells the file picker which extensions to offer', async () => {
    await openDialog();

    expect(input().accept).toBe('.png,.jpg,.jpeg,.gif,.webp,.mp4,.mov,.webm,.pdf,.mp3,.wav');
    expect(input().multiple).toBe(true);
  });

  it('cannot be closed while a file is being sent, and can be once the answer is in', async () => {
    let release: (response: Response) => void = () => undefined;
    const { dialog } = await openDialog({}, '/media-library', {
      'POST /v1/media/assets': () => new Promise<Response>((resolve) => { release = resolve; }) as unknown as Response,
    });

    choose(png('slow.png'));
    submit();

    expect(await within(dialog()).findByText('Uploading…', { selector: '.media-status' })).toBeTruthy();
    expect((within(dialog()).getByRole('button', { name: 'Close' }) as HTMLButtonElement).disabled).toBe(true);
    expect((within(dialog()).getByRole('button', { name: 'Cancel' }) as HTMLButtonElement).disabled).toBe(true);
    expect((within(dialog()).getByRole('button', { name: 'Choose files' }) as HTMLButtonElement).disabled).toBe(true);
    fireEvent.keyDown(document, { key: 'Escape' });
    expect(screen.getByRole('dialog')).toBeTruthy();

    await act(async () => release(jsonResponse(201, asset('slow1', { fileName: 'slow.png' }))));

    expect(await within(dialog()).findByText('1 file was uploaded.')).toBeTruthy();
    expect((within(dialog()).getByRole('button', { name: 'Done' }) as HTMLButtonElement).disabled).toBe(false);
  });
});

describe('an organization with several workspaces', () => {
  const two = [
    { id: 'b1000000', name: 'Northwind Foods', createdAt: '2026-03-05T10:30:00Z' },
    { id: 'b2000000', name: 'Contoso', createdAt: '2026-03-06T10:30:00Z' },
  ];

  it('shows one workspace at a time, the first by default, and the files of another when it is chosen', async () => {
    const assets = [...sample(), asset('c1', { workspaceId: 'b2000000', fileName: 'contoso-logo.png', createdAt: '2026-11-08T09:30:00.0000000+00:00' })];
    const { server } = await open({ workspaces: two, assets, member: { businessModel: 'agency' } });
    await filesLoaded();

    expect(names()).toEqual(['mountains.jpg', 'travel-video.mp4', 'brief.pdf', 'jingle.mp3']);
    select('Client', 'b2000000');

    await waitFor(() => expect(location()).toBe('/media-library?workspace=b2000000'));
    await waitFor(() => expect(names()).toEqual(['contoso-logo.png']));
    expect(server.requests.some((line) => line.includes('/overview?workspaceId=b2000000'))).toBe(true);
  });

  it('reads the workspace from the address, and falls back to the first for one the organization does not have', async () => {
    const { server } = await open({ workspaces: two }, '/media-library?workspace=00000000-0000-0000-0000-000000000000');
    await filesLoaded();

    expect(names()).toHaveLength(4);
    expect(server.requests.some((line) => line.includes('workspaceId=b1000000'))).toBe(true);
  });

  it('does not show a switcher when there is only one', async () => {
    await open();
    await filesLoaded();

    expect(screen.queryByLabelText('Product')).toBeNull();
    expect(screen.queryByLabelText('Client')).toBeNull();
  });
});

describe('while the workspaces load', () => {
  it('shows nothing of the library while the workspaces load, and does not break if they are slow', async () => {
    let release: (response: Response) => void = () => undefined;
    const server = mediaServer({ assets: sample() }, { 'GET /v1/workspaces': () => new Promise<Response>((resolve) => { release = resolve; }) as unknown as Response });
    await renderApp('/media-library', server.routes);

    expect(await heading('Media Library')).toBeTruthy();
    expect(screen.queryByRole('navigation', { name: 'Kind of media' })).toBeNull();
    await act(async () => release(jsonResponse(200, [{ id: 'b1000000', name: 'Northwind Foods', createdAt: '2026-03-05T10:30:00Z' }])));

    expect(await filesLoaded()).toBeTruthy();
  });
});
