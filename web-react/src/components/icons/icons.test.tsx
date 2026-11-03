import { render } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { readRepositoryFile } from '@/test/repo';
import { Icon } from './Icon';
import { iconNames, iconPaths, resolveIcon } from './icons';

// The server decides which icons a menu item may use. This reads its list, so one added there fails here until it is drawn.
const serverSource = readRepositoryFile('src/Platform.Auth/Services/MenuIcons.cs');
const serverIcons = [...(serverSource.match(/All\s*=\s*\[([^\]]*)\]/s)?.[1] ?? '').matchAll(/"([a-z][a-z0-9-]*)"/g)].map((match) => match[1]);

describe('iconNames', () => {
  it('finds the icons the server accepts', () => {
    expect(serverIcons).toHaveLength(10);
  });

  it('are exactly the icons the server accepts, in the same order', () => {
    expect(iconNames).toEqual(serverIcons);
  });
});

describe('resolveIcon', () => {
  it('keeps a name it can draw', () => {
    for (const name of iconNames) {
      expect(resolveIcon(name)).toBe(name);
    }
  });

  it('falls back to the circle for a name it cannot draw, and for anything that is not text', () => {
    for (const name of ['sparkles', 'Home', '', 'constructor', 'toString', '__proto__', undefined, null, 5, {}]) {
      expect(resolveIcon(name), String(name)).toBe('circle');
    }
  });
});

describe('Icon', () => {
  it('draws an svg that assistive technology skips and keyboard focus never enters', () => {
    const { container } = render(<Icon name="home" />);
    const svg = container.querySelector('svg');

    expect(svg?.getAttribute('aria-hidden')).toBe('true');
    expect(svg?.getAttribute('focusable')).toBe('false');
    expect(svg?.getAttribute('viewBox')).toBe('0 0 24 24');
    expect(svg?.getAttribute('class')).toBe('icon');
  });

  it('draws every icon with at least one stroke, each a path with a drawing', () => {
    for (const name of iconNames) {
      const paths = iconPaths(name);
      expect(paths.length, name).toBeGreaterThanOrEqual(1);
      for (const d of paths) {
        expect(d, name).toMatch(/^[MmLlHhVvCcSsQqTtAaZz0-9\s.,-]+$/);
      }
    }
  });

  it('draws the circle for a name it does not know, and different things for different icons', () => {
    expect(iconPaths('sparkles')).toEqual(iconPaths('circle'));
    expect(new Set(iconNames.map((name) => iconPaths(name).join('|'))).size).toBe(iconNames.length);
  });
});
