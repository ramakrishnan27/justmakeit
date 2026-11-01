import assert from 'node:assert/strict';
import { describe, it } from 'node:test';
import { decodeJwtPayload } from '../js/core/jwt.js';
import { makeJwt } from './helpers.js';

describe('decodeJwtPayload', () => {
  it('reads the claims of a token: the user id and the role', () => {
    const payload = decodeJwtPayload(makeJwt({ user_id: 'a1b2', role: 'siteadmin', exp: 1_900_000_000 }));

    assert.deepEqual(payload, { user_id: 'a1b2', role: 'siteadmin', exp: 1_900_000_000 });
  });

  it('reads claims with characters outside ASCII', () => {
    assert.equal(decodeJwtPayload(makeJwt({ name: 'Zoë – 山田' })).name, 'Zoë – 山田');
  });

  it('reads base64url payloads of every padding length', () => {
    for (const filler of ['', 'a', 'ab', 'abc', 'abcd']) {
      assert.equal(decodeJwtPayload(makeJwt({ filler })).filler, filler);
    }
  });

  it('returns null for anything that is not a readable token', () => {
    for (const value of [null, undefined, '', 'not a token', 'a.b', 'a..c', 'a.b.c.d', 42, {}, 'a.!!!.c']) {
      assert.equal(decodeJwtPayload(value), null, String(value));
    }
  });

  it('returns null when the payload is valid JSON but not an object', () => {
    for (const payload of [null, 7, 'text', [1, 2]]) {
      assert.equal(decodeJwtPayload(makeJwt(payload)), null, JSON.stringify(payload));
    }
  });
});
