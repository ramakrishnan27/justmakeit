// The editor's document, and what the page works out from it: its plain text, whether it is empty, whether a link may be
// followed. These mirror what the server does (RichTextDocument.cs): the server derives the plain text it stores and
// validates the document itself, and never trusts what this computes. The page uses them for the character counters, the
// previews and the enabling of buttons, so a mismatch can mislead a counter but never store anything wrong.

import type { RichDoc, RichNode } from '@/api/content';

/** Every kind of node the server accepts (RichTextDocument.cs). A test keeps this equal to the server's list. */
export const NODE_TYPES = ['blockquote', 'bulletList', 'doc', 'hardBreak', 'heading', 'listItem', 'orderedList', 'paragraph', 'text'] as const;

/** Every kind of mark the server accepts. */
export const MARK_TYPES = ['bold', 'italic', 'underline', 'link'] as const;

export const MAX_LINK_LENGTH = 2048;

/** A document with one empty paragraph: what the editor starts with. */
export function emptyDoc(): RichDoc {
  return { type: 'doc', content: [{ type: 'paragraph' }] };
}

/** A document of plain paragraphs, one per line of the text. For a template or a post that has text but no document. */
export function docFromText(text: string): RichDoc {
  const lines = text.length === 0 ? [''] : text.split(/\n{2,}/);
  return {
    type: 'doc',
    content: lines.map((line) => {
      const parts = line.split('\n');
      const content: RichNode[] = [];
      parts.forEach((part, index) => {
        if (index > 0) {
          content.push({ type: 'hardBreak' });
        }
        if (part.length > 0) {
          content.push({ type: 'text', text: part });
        }
      });
      return content.length === 0 ? { type: 'paragraph' } : { type: 'paragraph', content };
    }),
  };
}

/** Whether an address may be linked: http, https or mailto, no spaces or control characters, a sane length. Same rule as the server. */
export function isSafeLink(href: string | null | undefined): boolean {
  if (typeof href !== 'string' || href.length === 0 || href.length > MAX_LINK_LENGTH) {
    return false;
  }
  if (/[\s\u0000-\u001f\u007f-\u009f]/.test(href)) {
    return false;
  }
  try {
    const url = new URL(href);
    return url.protocol === 'http:' || url.protocol === 'https:' || url.protocol === 'mailto:';
  } catch {
    return false;
  }
}

const children = (node: RichNode): RichNode[] => (Array.isArray(node.content) ? node.content : []);

function inline(node: RichNode): string {
  return children(node)
    .map((child) => (child.type === 'text' ? (child.text ?? '') : child.type === 'hardBreak' ? '\n' : ''))
    .join('');
}

function listLines(list: RichNode, indent: string): string[] {
  const ordered = list.type === 'orderedList';
  const start = typeof list.attrs?.start === 'number' ? list.attrs.start : 1;
  let number = start;
  const lines: string[] = [];
  for (const item of children(list)) {
    const marker = ordered ? `${number++}.` : '•';
    let first = true;
    for (const part of children(item)) {
      if (part.type === 'bulletList' || part.type === 'orderedList') {
        lines.push(...listLines(part, `${indent}  `));
      } else if (first) {
        first = false;
        lines.push(`${indent}${marker} ${inline(part)}`);
      } else {
        lines.push(`${indent}  ${inline(part)}`);
      }
    }
  }
  return lines;
}

function blocks(node: RichNode, out: string[]) {
  switch (node.type) {
    case 'paragraph':
    case 'heading':
      out.push(inline(node));
      break;
    case 'blockquote':
      children(node).forEach((child) => blocks(child, out));
      break;
    case 'bulletList':
    case 'orderedList':
      out.push(listLines(node, '').join('\n'));
      break;
    default:
      break;
  }
}

/**
 * The plain text of a document: blocks separated by a blank line, list items one per line with their bullet or number, a line
 * break as a line break. What a platform receives has no formatting, and this is the text the character counters count.
 */
export function plainText(doc: RichDoc | null | undefined): string {
  if (!doc) {
    return '';
  }
  const out: string[] = [];
  children(doc).forEach((node) => blocks(node, out));
  return out.filter((block) => block.trim().length > 0).join('\n\n').trim();
}

export function isDocEmpty(doc: RichDoc | null | undefined): boolean {
  return plainText(doc).length === 0;
}

// --- Hashtags -----------------------------------------------------------------------------------------------------------

export const MAX_HASHTAG_LENGTH = 100;
export const MAX_HASHTAGS = 30;

/** A hashtag as it is stored: the leading #s and the spaces around them stripped. Null when what is left is not a hashtag. */
export function normalizeHashtag(raw: string): string | null {
  const text = raw.trim().replace(/^#+/, '').trim();
  return text.length >= 1 && text.length <= MAX_HASHTAG_LENGTH && !/[\s#\u0000-\u001f\u007f-\u009f]/.test(text) ? text : null;
}

/** Adds a hashtag to a list unless it is not one or the list already has it (letter case ignored). The same list when nothing was added. */
export function withHashtag(list: readonly string[], raw: string): readonly string[] {
  const tag = normalizeHashtag(raw);
  if (tag === null || list.length >= MAX_HASHTAGS || list.some((existing) => existing.toLowerCase() === tag.toLowerCase())) {
    return list;
  }
  return [...list, tag];
}

/**
 * The number of characters a platform will count for this text: the text, then the hashtags with their # one space apart, a blank
 * line between the two. Same as PlatformRules.CountedLength on the server.
 */
export function countedLength(text: string, hashtags: readonly string[]): number {
  if (hashtags.length === 0) {
    return text.length;
  }
  const tags = hashtags.reduce((sum, tag) => sum + tag.length + 1, 0) + (hashtags.length - 1);
  return text.length + 2 + tags;
}
