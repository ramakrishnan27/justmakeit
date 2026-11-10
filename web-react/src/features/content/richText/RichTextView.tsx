import type { ReactNode } from 'react';
import type { RichDoc, RichNode } from '@/api/content';
import { isSafeLink } from './doc';

const MAX_DEPTH = 12;

function marked(node: RichNode, key: string): ReactNode {
  let element: ReactNode = node.text ?? '';
  for (const mark of node.marks ?? []) {
    if (mark.type === 'bold') {
      element = <strong>{element}</strong>;
    } else if (mark.type === 'italic') {
      element = <em>{element}</em>;
    } else if (mark.type === 'underline') {
      element = <u>{element}</u>;
    } else if (mark.type === 'link') {
      const href = typeof mark.attrs?.href === 'string' ? mark.attrs.href : null;
      // A link is followed only if it is a web or mail address; anything else is just its text.
      if (isSafeLink(href)) {
        element = (
          <a href={href ?? undefined} target="_blank" rel="noopener noreferrer nofollow">
            {element}
          </a>
        );
      }
    }
  }
  return <span key={key}>{element}</span>;
}

function renderChildren(node: RichNode, path: string, depth: number): ReactNode[] {
  return (node.content ?? []).map((child, index) => render(child, `${path}.${index}`, depth + 1));
}

function render(node: RichNode, path: string, depth: number): ReactNode {
  if (depth > MAX_DEPTH) {
    return null;
  }
  switch (node.type) {
    case 'text':
      return marked(node, path);
    case 'hardBreak':
      return <br key={path} />;
    case 'paragraph':
      return <p key={path}>{renderChildren(node, path, depth)}</p>;
    case 'heading': {
      // The page has its own h1 and h2: the post's headings sit below them.
      const level = typeof node.attrs?.level === 'number' ? Math.min(3, Math.max(1, node.attrs.level)) : 1;
      return level === 1 ? (
        <h3 key={path}>{renderChildren(node, path, depth)}</h3>
      ) : level === 2 ? (
        <h4 key={path}>{renderChildren(node, path, depth)}</h4>
      ) : (
        <h5 key={path}>{renderChildren(node, path, depth)}</h5>
      );
    }
    case 'bulletList':
      return <ul key={path}>{renderChildren(node, path, depth)}</ul>;
    case 'orderedList': {
      const start = typeof node.attrs?.start === 'number' ? node.attrs.start : 1;
      return (
        <ol key={path} start={start}>
          {renderChildren(node, path, depth)}
        </ol>
      );
    }
    case 'listItem':
      return <li key={path}>{renderChildren(node, path, depth)}</li>;
    case 'blockquote':
      return <blockquote key={path}>{renderChildren(node, path, depth)}</blockquote>;
    default:
      // Not a kind the server accepts, so nothing is drawn for it.
      return null;
  }
}

/**
 * Draws the editor's document as React elements. There is no HTML anywhere: the document is data, each kind of part becomes an
 * element React builds and escapes, and a link is followed only if it is a web or mail address. Used for the live preview.
 */
export function RichTextView({ doc }: { doc: RichDoc | null }) {
  if (!doc) {
    return null;
  }
  return <div className="rich-text">{renderChildren(doc, 'n', 0)}</div>;
}
