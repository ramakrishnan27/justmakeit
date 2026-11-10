import { describe, expect, it } from 'vitest';
import { ApiError } from '@/api/client';
import {
  channelDescriptionMax,
  channelIconMaxBytes,
  channelNameMax,
  messageForChannelError,
  validateChannelForm,
  validateChannelIcon,
} from './channelForm';

const png = { type: 'image/png', size: 2_000 };

describe('validateChannelForm', () => {
  it('accepts a name alone, with or without a description and an icon', () => {
    expect(validateChannelForm('Telegram', '', null)).toBeNull();
    expect(validateChannelForm('  Telegram  ', 'Fast chat', png)).toBeNull();
  });

  it('asks for a name first', () => {
    expect(validateChannelForm('', 'x'.repeat(500), { type: 'image/gif', size: 1 })).toEqual({ field: 'name', message: 'Enter a name for the channel.' });
    expect(validateChannelForm('   ', '', null)?.field).toBe('name');
  });

  it('refuses a name or description that is too long, counted without the spaces round it', () => {
    expect(validateChannelForm('a'.repeat(channelNameMax), '', null)).toBeNull();
    expect(validateChannelForm(` ${'a'.repeat(channelNameMax)} `, '', null)).toBeNull();
    expect(validateChannelForm('a'.repeat(channelNameMax + 1), '', null)).toEqual({ field: 'name', message: 'The name must be at most 60 characters.' });
    expect(validateChannelForm('Telegram', 'b'.repeat(channelDescriptionMax), null)).toBeNull();
    expect(validateChannelForm('Telegram', 'b'.repeat(channelDescriptionMax + 1), null)).toEqual({
      field: 'description',
      message: 'The description must be at most 300 characters.',
    });
  });

  it('checks the icon after the text', () => {
    expect(validateChannelForm('Telegram', '', { type: 'image/svg+xml', size: 10 })).toEqual({
      field: 'icon',
      message: 'The icon must be a PNG, JPEG or WebP image.',
    });
  });
});

describe('validateChannelIcon', () => {
  it('accepts a PNG, a JPEG and a WebP of up to 100 KB, to the byte', () => {
    for (const type of ['image/png', 'image/jpeg', 'image/webp']) {
      expect(validateChannelIcon({ type, size: 1 }), type).toBeNull();
      expect(validateChannelIcon({ type, size: channelIconMaxBytes }), type).toBeNull();
    }
  });

  it('refuses anything else, however it is named: SVG and GIF most of all', () => {
    for (const type of ['image/svg+xml', 'image/gif', 'text/html', 'application/pdf', '', 'IMAGE/PNG']) {
      expect(validateChannelIcon({ type, size: 100 })?.message, type).toBe('The icon must be a PNG, JPEG or WebP image.');
    }
  });

  it('refuses an empty file and a file one byte over the limit', () => {
    expect(validateChannelIcon({ type: 'image/png', size: 0 })?.message).toBe('The icon file is empty.');
    expect(validateChannelIcon({ type: 'image/png', size: channelIconMaxBytes + 1 })?.message).toBe('The icon must be at most 100 KB.');
  });
});

describe('messageForChannelError', () => {
  it('puts a taken name under the name', () => {
    expect(messageForChannelError(new ApiError(409, 'x', { code: 'channel_exists' }))).toEqual({
      field: 'name',
      message: 'A channel with that name already exists.',
    });
  });

  it('puts what the server said about a field under that field, name first', () => {
    const errors = { icon: ['The icon file is empty.'], description: ['Too long.'], name: ['Name is required.'] };

    expect(messageForChannelError(new ApiError(400, 'x', { errors }))).toEqual({ field: 'name', message: 'Name is required.' });
    expect(messageForChannelError(new ApiError(400, 'x', { errors: { icon: ['The icon must be at most 100 KB.'] } }))).toEqual({
      field: 'icon',
      message: 'The icon must be at most 100 KB.',
    });
  });

  it('says what is wrong with an icon that is far too big, which the server refuses before reading it', () => {
    expect(messageForChannelError(new ApiError(413, 'x'))).toEqual({ field: 'icon', message: 'The icon must be at most 100 KB.' });
  });

  it('tells a person who is no longer a Site admin so, and a person who asks too often to wait', () => {
    expect(messageForChannelError(new ApiError(403, 'x', { code: 'not_site_admin' })).message).toBe('This account is no longer a site admin.');
    expect(messageForChannelError(new ApiError(429, 'x')).message).toMatch(/Wait a minute/);
  });

  it('keeps anything it does not know to the form as a whole, and says the server could not be reached for a network failure', () => {
    expect(messageForChannelError(new ApiError(500, 'x'))).toEqual({ field: 'form', message: 'The channel could not be added. Try again in a moment.' });
    expect(messageForChannelError(new ApiError(400, 'x', { errors: { other: ['hm'] } })).field).toBe('form');
    expect(messageForChannelError(new TypeError('network down'))).toEqual({
      field: 'form',
      message: 'Could not reach the server. Check your connection and try again.',
    });
  });
});
