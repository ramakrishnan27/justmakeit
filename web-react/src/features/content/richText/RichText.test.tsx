import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { createRef, useState } from 'react';
import { beforeAll, describe, expect, it } from 'vitest';
import type { RichDoc, RichNode } from '@/api/content';
import { readRepositoryFile } from '@/test/repo';
import { MARK_TYPES, NODE_TYPES, emptyDoc, plainText } from './doc';
import { RichTextEditor, linkFromInput, type RichTextEditorHandle } from './RichTextEditor';
import { RichTextView } from './RichTextView';

// ProseMirror measures the page when it moves the cursor; jsdom has no layout, so give it empty answers.
beforeAll(() => {
  const empty = () => ({ x: 0, y: 0, top: 0, left: 0, right: 0, bottom: 0, width: 0, height: 0, toJSON: () => ({}) });
  Range.prototype.getBoundingClientRect = empty;
  Range.prototype.getClientRects = () => ({ length: 0, item: () => null, [Symbol.iterator]: function* () {} }) as unknown as DOMRectList;
  document.elementFromPoint = () => null;
});

// The same table as RichTextDocument.cs: which kinds of part may sit directly inside which.
const allowed: Record<string, string[]> = {
  doc: ['paragraph', 'heading', 'bulletList', 'orderedList', 'blockquote'],
  paragraph: ['text', 'hardBreak'],
  heading: ['text', 'hardBreak'],
  bulletList: ['listItem'],
  orderedList: ['listItem'],
  listItem: ['paragraph', 'bulletList', 'orderedList'],
  blockquote: ['paragraph', 'heading', 'bulletList', 'orderedList', 'blockquote'],
  hardBreak: [],
  text: [],
};

/** What the server's validator would say about a document, for the shapes this editor makes. Empty when it would accept it. */
function problemsWith(node: RichNode, parent: string | null = null): string[] {
  const problems: string[] = [];
  if (!NODE_TYPES.includes(node.type as never)) {
    problems.push(`unknown node ${node.type}`);
    return problems;
  }
  if (parent !== null && !allowed[parent]?.includes(node.type)) {
    problems.push(`${node.type} inside ${parent}`);
  }
  for (const mark of node.marks ?? []) {
    if (!MARK_TYPES.includes(mark.type as never)) {
      problems.push(`unknown mark ${mark.type}`);
    }
  }
  for (const child of node.content ?? []) {
    problems.push(...problemsWith(child, node.type));
  }
  return problems;
}

function Harness({ initial = emptyDoc(), disabled = false, handle }: { initial?: RichDoc; disabled?: boolean; handle?: React.RefObject<RichTextEditorHandle | null> }) {
  const [doc, setDoc] = useState<RichDoc>(initial);
  return (
    <>
      <h2 id="label">Post Editor</h2>
      <RichTextEditor value={doc} onChange={setDoc} disabled={disabled} labelledBy="label" handleRef={handle} />
      <output data-testid="doc">{JSON.stringify(doc)}</output>
      <button type="button" onClick={() => setDoc({ type: 'doc', content: [{ type: 'paragraph', content: [{ type: 'text', text: 'From a template' }] }] })}>
        Replace
      </button>
    </>
  );
}

const current = (): RichDoc => JSON.parse(screen.getByTestId('doc').textContent ?? '{}') as RichDoc;
const surface = () => screen.getByRole('textbox', { name: 'Post Editor' });
const press = (name: string) => fireEvent.click(screen.getByRole('button', { name }));

describe('the rich text editor (Tiptap)', () => {
  it('shows the document it is given, as a text box named by the page', async () => {
    render(<Harness initial={{ type: 'doc', content: [{ type: 'paragraph', content: [{ type: 'text', text: 'Hello there' }] }] }} />);

    expect(await screen.findByText('Hello there')).toBeTruthy();
    expect(surface().getAttribute('aria-multiline')).toBe('true');
    expect(surface().getAttribute('contenteditable')).toBe('true');
    expect(screen.getByRole('toolbar', { name: 'Formatting' })).toBeTruthy();
  });

  it('writes no stylesheet of its own (the page policy forbids inline style)', async () => {
    render(<Harness />);
    await screen.findByRole('textbox', { name: 'Post Editor' });

    expect(document.head.querySelector('style[data-tiptap-style]')).toBeNull();
    expect(document.querySelectorAll('style')).toHaveLength(0);
  });

  it('reports typed text as a document the server accepts', async () => {
    const handle = createRef<RichTextEditorHandle>();
    render(<Harness handle={handle} />);
    await screen.findByRole('textbox', { name: 'Post Editor' });

    act(() => handle.current?.insertText('Typed words'));

    await waitFor(() => expect(plainText(current())).toBe('Typed words'));
    expect(problemsWith(current())).toEqual([]);
  });

  it('makes headings, lists and quotes in shapes the server accepts', async () => {
    const handle = createRef<RichTextEditorHandle>();
    render(<Harness handle={handle} />);
    await screen.findByRole('textbox', { name: 'Post Editor' });
    act(() => handle.current?.insertText('Some text'));
    await waitFor(() => expect(plainText(current())).toBe('Some text'));

    fireEvent.change(screen.getByRole('combobox', { name: 'Text style' }), { target: { value: '2' } });
    await waitFor(() => expect(current().content?.[0]).toMatchObject({ type: 'heading', attrs: { level: 2 } }));
    fireEvent.change(screen.getByRole('combobox', { name: 'Text style' }), { target: { value: '0' } });

    press('Bulleted list');
    await waitFor(() => expect(current().content?.[0]?.type).toBe('bulletList'));
    expect(problemsWith(current())).toEqual([]);
    press('Bulleted list');

    press('Numbered list');
    await waitFor(() => expect(current().content?.[0]?.type).toBe('orderedList'));
    expect(problemsWith(current())).toEqual([]);
    press('Numbered list');

    press('Quote');
    await waitFor(() => expect(current().content?.[0]?.type).toBe('blockquote'));
    expect(problemsWith(current())).toEqual([]);
    expect(plainText(current())).toBe('Some text');
  });

  it('makes bold, italic and underline marks, and no others', async () => {
    const handle = createRef<RichTextEditorHandle>();
    render(<Harness handle={handle} initial={{ type: 'doc', content: [{ type: 'paragraph', content: [{ type: 'text', text: 'Emphasis' }] }] }} />);
    const box = await screen.findByRole('textbox', { name: 'Post Editor' });

    // jsdom has no real selection, so select the word through the editor itself (Tiptap keeps itself on the element), then use each tool.
    act(() => {
      (box as unknown as { editor: { commands: { selectAll: () => boolean } } }).editor.commands.selectAll();
    });

    press('Bold');
    press('Italic');
    press('Underline');
    await waitFor(() => {
      const marks = (current().content?.[0]?.content?.[0]?.marks ?? []).map((mark) => mark.type).sort();
      expect(marks).toEqual(['bold', 'italic', 'underline']);
    });
    expect(problemsWith(current())).toEqual([]);
  });

  it('writes only attributes the server accepts (found by the real browser: Tiptap 3 adds a title to every link)', async () => {
    const t = (text: string, marks: RichNode['marks'] = []): RichNode => ({ type: 'text', text, marks });
    const handle = createRef<RichTextEditorHandle>();
    render(
      <Harness
        handle={handle}
        initial={{
          type: 'doc',
          content: [
            { type: 'heading', attrs: { level: 2 }, content: [t('Head')] },
            { type: 'paragraph', content: [t('a', [{ type: 'bold' }, { type: 'italic' }, { type: 'underline' }, { type: 'link', attrs: { href: 'https://example.com' } }]), { type: 'hardBreak' }, t('b')] },
            { type: 'bulletList', content: [{ type: 'listItem', content: [{ type: 'paragraph', content: [t('dot')] }] }] },
            { type: 'orderedList', content: [{ type: 'listItem', content: [{ type: 'paragraph', content: [t('one')] }] }] },
            { type: 'blockquote', content: [{ type: 'paragraph', content: [t('quote')] }] },
          ],
        }}
      />,
    );
    await screen.findByRole('textbox', { name: 'Post Editor' });
    act(() => handle.current?.insertText('!'));
    await waitFor(() => expect(plainText(current())).toContain('!'));

    // What the server's validator names, read from its source.
    const source = readRepositoryFile('src/Platform.Content/Features/Posts/RichTextDocument.cs');
    const linkRule = source.slice(source.indexOf('private static string? ValidateLink'), source.indexOf('public static bool IsSafeLink'));
    const accepted: Record<string, string[]> = {
      link: [...linkRule.matchAll(/case "(\w+)" when/g)].map((match) => match[1] ?? ''),
      heading: ['level'],
      orderedList: [...source.matchAll(/"(start|type)" =>/g)].map((match) => match[1] ?? ''),
    };
    expect(accepted.link).toEqual(expect.arrayContaining(['href', 'target', 'rel', 'class', 'title']));
    expect([...(accepted.orderedList ?? [])].sort()).toEqual(['start', 'type']);

    const written: Record<string, Set<string>> = {};
    const note = (kind: string, attrs: Record<string, unknown> | undefined) => {
      written[kind] ??= new Set();
      Object.keys(attrs ?? {}).forEach((key) => written[kind]?.add(key));
    };
    const walk = (node: RichNode) => {
      note(node.type, node.attrs);
      (node.marks ?? []).forEach((mark) => note(mark.type, mark.attrs));
      (node.content ?? []).forEach(walk);
    };
    walk(current());

    expect(Object.keys(written).sort()).toEqual(expect.arrayContaining(['link', 'heading', 'orderedList', 'bold', 'blockquote']));
    for (const [kind, keys] of Object.entries(written)) {
      const extra = [...keys].filter((key) => !(accepted[kind] ?? []).includes(key));
      expect(extra, `attributes written for ${kind} that the server does not accept`).toEqual([]);
    }
  });

  it('reports no change when it only shows a document: opening a saved post must not look like an edit', async () => {
    const t = (text: string, marks: RichNode['marks'] = []): RichNode => ({ type: 'text', text, marks });
    const saved: RichDoc = {
      type: 'doc',
      content: [
        { type: 'paragraph', content: [t('saved', [{ type: 'link', attrs: { href: 'https://example.com' } }])] },
        { type: 'orderedList', content: [{ type: 'listItem', content: [{ type: 'paragraph', content: [t('one')] }] }] },
      ],
    };
    const changes: RichDoc[] = [];
    const element = (disabled: boolean) => <RichTextEditor value={saved} onChange={(doc) => changes.push(doc)} disabled={disabled} labelledBy="label" />;
    const { rerender } = render(element(false));
    await screen.findByRole('textbox');

    // Tiptap fills in attributes it defaults (a link's title, a list's start); the page compares what it holds with what was saved,
    // so a document the person did not touch must not be reported back at all. Nor must switching the editor off and on again.
    rerender(element(true));
    rerender(element(false));
    await new Promise((resolve) => setTimeout(resolve, 50));

    expect(changes).toEqual([]);
  });
  it('refuses a link that is not a web or mail address, and says why', async () => {
    render(<Harness />);
    await screen.findByRole('textbox', { name: 'Post Editor' });

    press('Link');
    fireEvent.change(screen.getByLabelText('Link address'), { target: { value: 'javascript:alert(1)' } });
    fireEvent.click(screen.getByRole('button', { name: 'Apply' }));

    expect(screen.getByRole('alert').textContent).toMatch(/web address/);
    expect(plainText(current())).toBe('');
    expect(JSON.stringify(current())).not.toContain('javascript');
  });

  it('adds a link written without a scheme as https', async () => {
    render(<Harness />);
    await screen.findByRole('textbox', { name: 'Post Editor' });

    press('Link');
    fireEvent.change(screen.getByLabelText('Link address'), { target: { value: 'example.com/offer' } });
    fireEvent.click(screen.getByRole('button', { name: 'Apply' }));

    await waitFor(() => {
      const text = current().content?.[0]?.content?.[0];
      expect(text).toMatchObject({ type: 'text', text: 'https://example.com/offer' });
      expect(text?.marks?.[0]).toMatchObject({ type: 'link', attrs: { href: 'https://example.com/offer' } });
    });
    expect(problemsWith(current())).toEqual([]);
  });

  it('has an emoji picker that types the emoji', async () => {
    render(<Harness />);
    await screen.findByRole('textbox', { name: 'Post Editor' });

    press('Emoji');
    const picker = screen.getByRole('group', { name: 'Emoji' });
    fireEvent.click(within(picker).getAllByRole('button')[0] as HTMLElement);

    await waitFor(() => expect(plainText(current()).length).toBeGreaterThan(0));
  });

  it('takes a document that did not come from typing (a template), and does not loop on its own', async () => {
    render(<Harness />);
    await screen.findByRole('textbox', { name: 'Post Editor' });

    fireEvent.click(screen.getByRole('button', { name: 'Replace' }));

    expect(await screen.findByText('From a template')).toBeTruthy();
    expect(plainText(current())).toBe('From a template');
  });

  it('cannot be typed into when it is switched off', async () => {
    render(<Harness disabled />);
    const box = await screen.findByRole('textbox', { name: 'Post Editor' });

    expect(box.getAttribute('contenteditable')).toBe('false');
    expect((screen.getByRole('button', { name: 'Bold' }) as HTMLButtonElement).disabled).toBe(true);
    expect((screen.getByRole('combobox', { name: 'Text style' }) as HTMLSelectElement).disabled).toBe(true);
  });
});

describe('linkFromInput', () => {
  it('keeps a web or mail address, adds https to a bare one, and refuses the rest', () => {
    expect(linkFromInput(' https://example.com ')).toBe('https://example.com');
    expect(linkFromInput('mailto:a@example.com')).toBe('mailto:a@example.com');
    expect(linkFromInput('example.com')).toBe('https://example.com');
    expect(linkFromInput('javascript:alert(1)')).toBeNull();
    expect(linkFromInput('data:text/html,hi')).toBeNull();
    expect(linkFromInput('two words.com')).toBeNull();
    expect(linkFromInput('   ')).toBeNull();
  });
});

describe('RichTextView', () => {
  const doc = (content: RichNode[]): RichDoc => ({ type: 'doc', content });
  const text = (value: string, marks: RichNode['marks'] = []): RichNode => ({ type: 'text', text: value, marks });

  it('draws each kind of part as its own element, and the headings below the page\'s own', () => {
    const { container } = render(
      <RichTextView
        doc={doc([
          { type: 'heading', attrs: { level: 1 }, content: [text('One')] },
          { type: 'heading', attrs: { level: 2 }, content: [text('Two')] },
          { type: 'heading', attrs: { level: 3 }, content: [text('Three')] },
          { type: 'paragraph', content: [text('Bold', [{ type: 'bold' }]), { type: 'hardBreak' }, text('Em', [{ type: 'italic' }, { type: 'underline' }])] },
          { type: 'orderedList', attrs: { start: 4 }, content: [{ type: 'listItem', content: [{ type: 'paragraph', content: [text('four')] }] }] },
          { type: 'bulletList', content: [{ type: 'listItem', content: [{ type: 'paragraph', content: [text('dot')] }] }] },
          { type: 'blockquote', content: [{ type: 'paragraph', content: [text('quoted')] }] },
        ])}
      />,
    );

    expect(container.querySelector('h3')?.textContent).toBe('One');
    expect(container.querySelector('h4')?.textContent).toBe('Two');
    expect(container.querySelector('h5')?.textContent).toBe('Three');
    expect(container.querySelector('h1, h2')).toBeNull();
    expect(container.querySelector('strong')?.textContent).toBe('Bold');
    expect(container.querySelector('em u, u em')?.textContent).toBe('Em');
    expect(container.querySelector('br')).not.toBeNull();
    expect(container.querySelector('ol')?.getAttribute('start')).toBe('4');
    expect(container.querySelector('ul li')?.textContent).toBe('dot');
    expect(container.querySelector('blockquote')?.textContent).toBe('quoted');
  });

  it('follows a web link in a new tab without leaking the page, and not any other kind', () => {
    const { container } = render(
      <RichTextView
        doc={doc([
          {
            type: 'paragraph',
            content: [
              text('good', [{ type: 'link', attrs: { href: 'https://example.com/a' } }]),
              text('bad', [{ type: 'link', attrs: { href: 'javascript:alert(1)' } }]),
              text('data', [{ type: 'link', attrs: { href: 'data:text/html,<b>x</b>' } }]),
              text('none', [{ type: 'link', attrs: {} }]),
            ],
          },
        ])}
      />,
    );

    const links = container.querySelectorAll('a');
    expect(links).toHaveLength(1);
    expect(links[0]?.getAttribute('href')).toBe('https://example.com/a');
    expect(links[0]?.getAttribute('target')).toBe('_blank');
    expect(links[0]?.getAttribute('rel')).toBe('noopener noreferrer nofollow');
    // What cannot be followed is still shown, as its text.
    expect(container.textContent).toBe('goodbaddatanone');
  });

  it('shows markup in the text as text, and draws nothing for a kind of part it does not know', () => {
    const { container } = render(
      <RichTextView
        doc={doc([
          { type: 'paragraph', content: [text('<img src=x onerror=alert(1)><script>alert(2)</script>')] },
          { type: 'codeBlock', content: [text('secret')] },
          { type: 'image', attrs: { src: 'https://example.com/x.png' } },
        ])}
      />,
    );

    expect(container.querySelector('img, script, pre, code')).toBeNull();
    expect(container.textContent).toContain('<img src=x onerror=alert(1)><script>alert(2)</script>');
    expect(container.textContent).not.toContain('secret');
  });

  it('stops at a depth no editor makes, instead of drawing without end', () => {
    let nested: RichNode = { type: 'paragraph', content: [text('deep')] };
    for (let i = 0; i < 40; i++) {
      nested = { type: 'blockquote', content: [nested] };
    }
    const { container } = render(<RichTextView doc={doc([nested])} />);

    expect(container.querySelectorAll('blockquote').length).toBeLessThan(40);
    expect(container.textContent).not.toContain('deep');
  });

  it('draws nothing for no document', () => {
    const { container } = render(<RichTextView doc={null} />);
    expect(container.innerHTML).toBe('');
  });
});
