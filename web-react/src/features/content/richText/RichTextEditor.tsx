import Placeholder from '@tiptap/extension-placeholder';
import { EditorContent, useEditor, useEditorState } from '@tiptap/react';
import StarterKit from '@tiptap/starter-kit';
import { useEffect, useImperativeHandle, useRef, useState, type FormEvent, type ReactNode, type Ref } from 'react';
import type { RichDoc } from '@/api/content';
import { isSafeLink } from './doc';
import { EMOJI } from './emoji';

/** What a page can ask of the editor from outside. */
export interface RichTextEditorHandle {
  /** Types the text at the cursor (an emoji from the page's own button). */
  insertText: (text: string) => void;
  focus: () => void;
  /** Opens or closes the emoji picker. */
  toggleEmoji: () => void;
}

/** An address a person typed into the link box, made into one that can be linked: a bare "example.com" becomes https://example.com. */
export function linkFromInput(raw: string): string | null {
  const text = raw.trim();
  if (text.length === 0) {
    return null;
  }
  const withScheme = /^[a-z][a-z0-9+.-]*:/i.test(text) ? text : `https://${text}`;
  return isSafeLink(withScheme) ? withScheme : null;
}

/**
 * The post editor: rich text (paragraphs, headings, bold, italic, underline, lists, quotes, links) on Tiptap, kept to exactly the
 * kinds of part the server accepts, so what it produces is never refused for its shape. The document goes out through `onChange` as
 * JSON data; there is no HTML anywhere. Tiptap writes no stylesheet of its own (the page's content security policy forbids it):
 * the few rules it needs are in the app's stylesheet.
 */
export function RichTextEditor({
  value,
  onChange,
  disabled = false,
  labelledBy,
  placeholder = 'Write your post…',
  handleRef,
}: {
  value: RichDoc;
  onChange: (doc: RichDoc) => void;
  disabled?: boolean;
  /** The id of the element that names the editor. */
  labelledBy: string;
  placeholder?: string;
  handleRef?: Ref<RichTextEditorHandle>;
}) {
  // The last document the editor itself produced, so a value that comes back from the page is not poured into the editor again.
  const emitted = useRef<string>(JSON.stringify(value));
  const [emojiOpen, setEmojiOpen] = useState(false);
  const [linkOpen, setLinkOpen] = useState(false);
  const [linkText, setLinkText] = useState('');
  const [linkProblem, setLinkProblem] = useState<string | null>(null);

  const editor = useEditor({
    extensions: [
      StarterKit.configure({
        heading: { levels: [1, 2, 3] },
        // Not offered, and not accepted by the server: no code, no strikethrough, no rules, no end-of-document filler.
        code: false,
        codeBlock: false,
        strike: false,
        horizontalRule: false,
        trailingNode: false,
        link: { openOnClick: false, autolink: false, linkOnPaste: false, defaultProtocol: 'https', isAllowedUri: (url: string) => isSafeLink(url) },
      }),
      Placeholder.configure({ placeholder }),
    ],
    content: value,
    editable: !disabled,
    injectCSS: false,
    editorProps: {
      attributes: { role: 'textbox', 'aria-multiline': 'true', 'aria-labelledby': labelledBy, class: 'rich-editor-content' },
    },
    onUpdate: ({ editor: current }) => {
      const doc = current.getJSON() as RichDoc;
      emitted.current = JSON.stringify(doc);
      onChange(doc);
    },
  });

  // A document that did not come from typing (a template, a post that was loaded) replaces what is in the editor.
  useEffect(() => {
    if (!editor) {
      return;
    }
    const incoming = JSON.stringify(value);
    if (incoming !== emitted.current) {
      editor.commands.setContent(value, { emitUpdate: false });
      emitted.current = incoming;
    }
  }, [editor, value]);

  useEffect(() => {
    // Without `false`, Tiptap announces an update, and the page would be told about Tiptap's own version of a document nobody
    // touched (with the attributes it fills in), and take it for an unsaved change.
    if (editor && editor.isEditable === disabled) {
      editor.setEditable(!disabled, false);
    }
  }, [editor, disabled]);

  useImperativeHandle(
    handleRef,
    () => ({
      insertText: (text) => void editor?.chain().focus().insertContent(text).run(),
      focus: () => void editor?.commands.focus(),
      toggleEmoji: () => setEmojiOpen((open) => !open),
    }),
    [editor],
  );

  const active = useEditorState({
    editor,
    selector: ({ editor: current }) => ({
      bold: current?.isActive('bold') ?? false,
      italic: current?.isActive('italic') ?? false,
      underline: current?.isActive('underline') ?? false,
      bullet: current?.isActive('bulletList') ?? false,
      ordered: current?.isActive('orderedList') ?? false,
      quote: current?.isActive('blockquote') ?? false,
      link: current?.isActive('link') ?? false,
      heading: [1, 2, 3].find((level) => current?.isActive('heading', { level })) ?? 0,
      canUndo: current?.can().undo() ?? false,
      canRedo: current?.can().redo() ?? false,
    }),
  });

  if (!editor) {
    return null;
  }

  const keepSelection = (event: FormEvent) => event.preventDefault();

  function openLink() {
    setLinkText((editor?.getAttributes('link').href as string | undefined) ?? '');
    setLinkProblem(null);
    setLinkOpen(true);
    setEmojiOpen(false);
  }

  function applyLink(event: FormEvent) {
    event.preventDefault();
    if (!editor) {
      return;
    }
    if (linkText.trim().length === 0) {
      editor.chain().focus().extendMarkRange('link').unsetLink().run();
      setLinkOpen(false);
      return;
    }
    const href = linkFromInput(linkText);
    if (href === null) {
      setLinkProblem('Enter a web address (starting with http:// or https://) or an email address (mailto:).');
      return;
    }
    if (editor.state.selection.empty && !editor.isActive('link')) {
      editor.chain().focus().insertContent({ type: 'text', text: href, marks: [{ type: 'link', attrs: { href } }] }).run();
    } else {
      editor.chain().focus().extendMarkRange('link').setLink({ href }).run();
    }
    setLinkOpen(false);
  }

  const tool = (label: string, pressed: boolean, run: () => void, content: ReactNode, extra = '') => (
    <button
      key={label}
      type="button"
      className={`tool${extra}`}
      aria-label={label}
      aria-pressed={pressed}
      disabled={disabled}
      onMouseDown={keepSelection}
      onClick={run}
    >
      {content}
    </button>
  );

  return (
    <div className="rich-editor">
      <div className="rich-toolbar" role="toolbar" aria-label="Formatting" aria-controls="editor-surface">
        <select
          className="tool-select"
          aria-label="Text style"
          value={active.heading}
          disabled={disabled}
          onChange={(event) => {
            const level = Number(event.target.value);
            if (level === 0) {
              editor.chain().focus().setParagraph().run();
            } else {
              editor.chain().focus().setHeading({ level: level as 1 | 2 | 3 }).run();
            }
          }}
        >
          <option value={0}>Paragraph</option>
          <option value={1}>Heading 1</option>
          <option value={2}>Heading 2</option>
          <option value={3}>Heading 3</option>
        </select>
        {tool('Bold', active.bold, () => editor.chain().focus().toggleBold().run(), <strong>B</strong>)}
        {tool('Italic', active.italic, () => editor.chain().focus().toggleItalic().run(), <em>I</em>)}
        {tool('Underline', active.underline, () => editor.chain().focus().toggleUnderline().run(), <u>U</u>)}
        {tool('Bulleted list', active.bullet, () => editor.chain().focus().toggleBulletList().run(), '• List')}
        {tool('Numbered list', active.ordered, () => editor.chain().focus().toggleOrderedList().run(), '1. List')}
        {tool('Quote', active.quote, () => editor.chain().focus().toggleBlockquote().run(), '“ Quote')}
        {tool('Link', active.link, openLink, 'Link')}
        {tool('Emoji', emojiOpen, () => setEmojiOpen((open) => !open), '🙂')}
        {tool('Undo', false, () => editor.chain().focus().undo().run(), '↶', active.canUndo ? '' : ' is-off')}
        {tool('Redo', false, () => editor.chain().focus().redo().run(), '↷', active.canRedo ? '' : ' is-off')}
      </div>

      {linkOpen ? (
        <form className="rich-popover" onSubmit={applyLink}>
          <label htmlFor="link-address">Link address</label>
          <div className="input-row">
            <input
              id="link-address"
              type="text"
              inputMode="url"
              autoComplete="off"
              placeholder="https://example.com"
              value={linkText}
              onChange={(event) => setLinkText(event.target.value)}
              aria-describedby={linkProblem ? 'link-problem' : undefined}
              autoFocus
            />
            <button className="button button-primary" type="submit">
              Apply
            </button>
            <button className="button" type="button" onClick={() => setLinkOpen(false)}>
              Cancel
            </button>
          </div>
          {linkProblem ? (
            <p className="form-error" id="link-problem" role="alert">
              {linkProblem}
            </p>
          ) : null}
          <p className="hint">Leave it empty and press Apply to remove the link.</p>
        </form>
      ) : null}

      {emojiOpen ? (
        <div className="rich-popover emoji-picker" role="group" aria-label="Emoji">
          {EMOJI.map((emoji) => (
            <button
              key={emoji}
              type="button"
              className="emoji"
              aria-label={`Insert ${emoji}`}
              disabled={disabled}
              onMouseDown={keepSelection}
              onClick={() => editor.chain().focus().insertContent(emoji).run()}
            >
              {emoji}
            </button>
          ))}
        </div>
      ) : null}

      <div id="editor-surface" className="rich-surface">
        <EditorContent editor={editor} />
      </div>
    </div>
  );
}
