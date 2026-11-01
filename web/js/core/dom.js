// Builds DOM nodes without innerHTML. Text always goes in as text, so nothing the API returns can
// ever be read as markup.

/**
 * @param {string} tag
 * @param {Record<string, unknown>} [props] `class`, `on<Event>` handlers, `text`, or any attribute.
 *   false, null and undefined skip the attribute; true sets it empty (for example `hidden`).
 * @param {...(Node | string | number | null | undefined | false | Array<unknown>)} children
 */
export function h(tag, props = {}, ...children) {
  const element = document.createElement(tag);

  for (const [name, value] of Object.entries(props ?? {})) {
    if (value === undefined || value === null || value === false) {
      continue;
    }
    if (name === 'class') {
      element.className = String(value);
    } else if (name === 'text') {
      element.textContent = String(value);
    } else if (name.startsWith('on') && typeof value === 'function') {
      element.addEventListener(name.slice(2).toLowerCase(), value);
    } else {
      element.setAttribute(name, value === true ? '' : String(value));
    }
  }

  for (const child of children.flat()) {
    if (child === undefined || child === null || child === false) {
      continue;
    }
    element.append(child instanceof Node ? child : document.createTextNode(String(child)));
  }

  return element;
}

/** Replaces everything inside a node. */
export function replaceChildren(parent, ...children) {
  parent.replaceChildren(...children.flat().filter((child) => child !== undefined && child !== null && child !== false));
}
