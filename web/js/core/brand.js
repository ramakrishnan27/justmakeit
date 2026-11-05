import { h } from './dom.js';

/** The logo and name at the top of the member sign-in and sign-up pages. */
export function authHeader() {
  return h(
    'div',
    { class: 'auth-header' },
    h('img', { class: 'brand-mark', src: '/brand-mark.svg', alt: '', width: '32', height: '32' }),
    h('span', {}, 'Platform'),
  );
}
