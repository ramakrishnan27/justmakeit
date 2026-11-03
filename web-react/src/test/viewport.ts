// jsdom has no layout, so a media query never matches by itself. The tests ask for a wide or a narrow screen here,
// and every component that listens to the query hears about the change.

let wide = true;
const listeners = new Set<() => void>();

export function installMatchMedia() {
  window.matchMedia = (query: string): MediaQueryList =>
    ({
      media: query,
      get matches() {
        return wide;
      },
      onchange: null,
      addEventListener: (_type: string, listener: () => void) => {
        listeners.add(listener);
      },
      removeEventListener: (_type: string, listener: () => void) => {
        listeners.delete(listener);
      },
      addListener: () => undefined,
      removeListener: () => undefined,
      dispatchEvent: () => false,
    }) as unknown as MediaQueryList;
}

/** Pretends the screen is at least 720 px wide (true, the default) or narrower. */
export function setWideScreen(value: boolean) {
  wide = value;
  listeners.forEach((listener) => listener());
}
