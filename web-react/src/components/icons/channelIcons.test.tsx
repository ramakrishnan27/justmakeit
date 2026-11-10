import { fireEvent, render } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { channelIconAccept, channelIconMaxBytes, channelIconTypes, channelIconUrl } from '@/lib/channelForm';
import { readRepositoryFile } from '@/test/repo';
import { ChannelIcon } from './ChannelIcon';
import { channelGlyphNames, channelIconPaths } from './channelIcons';
import { iconPaths } from './icons';

const pathData = /^[MmLlHhVvCcSsQqTtAaZz0-9\s.,-]+$/;

describe('channelIconPaths', () => {
  it('draws every built-in channel icon with at least one stroke, each a path with a drawing', () => {
    expect(channelGlyphNames.length).toBeGreaterThanOrEqual(8);
    for (const name of channelGlyphNames) {
      const paths = channelIconPaths(name);
      expect(paths.length, name).toBeGreaterThanOrEqual(1);
      for (const d of paths) {
        expect(d, name).toMatch(pathData);
      }
    }
  });

  it('draws a different thing for each built-in channel icon', () => {
    expect(new Set(channelGlyphNames.map((name) => channelIconPaths(name).join('|'))).size).toBe(channelGlyphNames.length);
  });

  it('falls back to the plain circle for a name it has no drawing for, however odd the name', () => {
    for (const key of ['tiktok', '', 'Instagram', 'constructor', 'toString', '__proto__', null, undefined]) {
      expect(channelIconPaths(key), String(key)).toEqual(iconPaths('circle'));
    }
  });
});

describe('the built-in icons and the server', () => {
  it('has a drawing for the icon of every channel the migration seeds', () => {
    const script = readRepositoryFile('src/Platform.Core/Infrastructure/Migrations/011_channels.sql');
    const seeded = [...script.matchAll(/\(\d+,\s*'[^']+',\s*'[^']*',\s*'([a-z][a-z0-9-]*)'\)/g)].map((match) => match[1]);

    expect(seeded).toEqual(['instagram', 'facebook', 'youtube', 'email', 'sms', 'whatsapp', 'linkedin']);
    for (const key of seeded) {
      expect(channelGlyphNames, `${key} has no drawing`).toContain(key);
    }
  });

  it('has a drawing for the icon the server gives a channel that has no uploaded image', () => {
    const rules = readRepositoryFile('src/Platform.Core/Auth/Services/ChannelIconRules.cs');
    const fallback = /DefaultGlyph\s*=\s*"([a-z][a-z0-9-]*)"/.exec(rules)?.[1];

    expect(fallback).toBe('broadcast');
    expect(channelGlyphNames).toContain(fallback);
  });
});

describe('the icon an admin may upload, and the server', () => {
  const rules = readRepositoryFile('src/Platform.Core/Auth/Services/ChannelIconRules.cs');

  it('allows the same size as the server', () => {
    const kilobytes = /MaxBytes\s*=\s*(\d+)\s*\*\s*1024/.exec(rules)?.[1];

    expect(kilobytes).toBe('100');
    expect(channelIconMaxBytes).toBe(Number(kilobytes) * 1024);
  });

  it('allows the same image types as the server, and neither SVG nor GIF', () => {
    const serverTypes = [...rules.matchAll(/return\s+"(image\/[a-z]+)"/g)].map((match) => match[1]);

    expect(serverTypes).toEqual(['image/png', 'image/jpeg', 'image/webp']);
    expect([...channelIconTypes]).toEqual(serverTypes);
    expect(channelIconAccept).toBe('image/png,image/jpeg,image/webp');
    expect(channelIconTypes.join(' ')).not.toMatch(/svg|gif/);
  });

  it('serves an uploaded icon from the route the server has for it', () => {
    const controller = readRepositoryFile('src/Platform.Core/Auth/Controllers/SiteChannelsController.cs');

    expect(controller).toContain('[Route("v1/site/channels")]');
    expect(controller).toContain('[HttpGet("{id:guid}/icon")]');
    expect(channelIconUrl('11111111-1111-1111-1111-111111111111')).toBe('/v1/site/channels/11111111-1111-1111-1111-111111111111/icon');
  });
});

describe('ChannelIcon', () => {
  it('draws a built-in icon as an svg that assistive technology skips and keyboard focus never enters', () => {
    const { container } = render(<ChannelIcon channel={{ id: 'c1', iconKey: 'instagram', hasIconImage: false }} />);
    const svg = container.querySelector('svg');

    expect(svg?.getAttribute('aria-hidden')).toBe('true');
    expect(svg?.getAttribute('focusable')).toBe('false');
    expect(svg?.getAttribute('viewBox')).toBe('0 0 24 24');
    expect(svg?.getAttribute('class')).toBe('icon channel-icon');
    expect(svg?.querySelectorAll('path').length).toBeGreaterThanOrEqual(1);
    expect(container.querySelector('img')).toBeNull();
  });

  it('draws an uploaded image from the server, with no alternative text (the name beside it says it), never an svg', () => {
    const { container } = render(<ChannelIcon channel={{ id: 'c 1/2', iconKey: null, hasIconImage: true }} />);
    const image = container.querySelector('img');

    expect(image?.getAttribute('src')).toBe('/v1/site/channels/c%201%2F2/icon');
    expect(image?.getAttribute('alt')).toBe('');
    expect(image?.getAttribute('class')).toBe('channel-image');
    expect(container.querySelector('svg')).toBeNull();
  });

  it('falls back to the built-in drawing, or the circle, when the uploaded image cannot be loaded', () => {
    const { container } = render(<ChannelIcon channel={{ id: 'c1', iconKey: null, hasIconImage: true }} />);

    fireEvent.error(container.querySelector('img')!);

    expect(container.querySelector('img')).toBeNull();
    expect(container.querySelector('svg')?.querySelectorAll('path')).toHaveLength(iconPaths('circle').length);
  });

  it('draws the circle for an icon name it does not know', () => {
    const { container } = render(<ChannelIcon channel={{ id: 'c1', iconKey: 'tiktok', hasIconImage: false }} />);

    expect(container.querySelector('svg path')?.getAttribute('d')).toBe(iconPaths('circle')[0]);
  });
});
