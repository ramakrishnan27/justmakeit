import assert from 'node:assert/strict';
import { describe, it } from 'node:test';
import { ApiError } from '../js/core/api.js';
import {
  cellLabel,
  groupSlots,
  isGranted,
  messageForMatrixLoadError,
  messageForPermissionError,
  withGrant,
} from '../js/core/permissions.js';

// The slots as the server sends them: each kind of organization with the roles it allows.
const slots = [
  { businessModel: 'company', role: 'organizationadmin' },
  { businessModel: 'company', role: 'marketingmanager' },
  { businessModel: 'company', role: 'contentwriter' },
  { businessModel: 'agency', role: 'organizationadmin' },
  { businessModel: 'agency', role: 'clientmanager' },
  { businessModel: 'agency', role: 'marketingmanager' },
  { businessModel: 'agency', role: 'contentwriter' },
  { businessModel: 'creator', role: 'contentwriter' },
];

function matrix() {
  return {
    slots,
    permissions: [
      {
        key: 'workspaces.create',
        title: 'Create workspaces',
        description: 'Add a workspace.',
        isProtected: false,
        grants: slots.map((slot) => ({ ...slot, granted: slot.role === 'organizationadmin' })),
      },
      {
        key: 'dashboard.view',
        title: 'View the dashboard',
        description: 'Home.',
        isProtected: true,
        grants: slots.map((slot) => ({ ...slot, granted: true })),
      },
    ],
  };
}

describe('groupSlots', () => {
  it('groups the roles by kind of organization, in the order the server sends them', () => {
    const groups = groupSlots(slots);

    assert.deepEqual(
      groups.map((group) => group.businessModel),
      ['company', 'agency', 'creator'],
    );
    assert.deepEqual(
      groups.map((group) => group.roles.length),
      [3, 4, 1],
    );
  });

  it('gives each group and each role the words people read', () => {
    const [company, agency, creator] = groupSlots(slots);

    assert.equal(company.title, 'Company');
    assert.equal(agency.title, 'Marketing agency');
    assert.equal(creator.title, 'Individual creator');
    assert.deepEqual(
      agency.roles.map((role) => role.title),
      ['Organization Admin', 'Client Manager', 'Marketing Manager', 'Content Writer'],
    );
    assert.deepEqual(
      agency.roles.map((role) => role.role),
      ['organizationadmin', 'clientmanager', 'marketingmanager', 'contentwriter'],
    );
  });

  it('shows a kind of organization or role it does not know as given, so a newer server leaves no blank', () => {
    const [group] = groupSlots([{ businessModel: 'franchise', role: 'regionalmanager' }]);

    assert.equal(group.title, 'Franchise');
    assert.equal(group.roles[0].title, 'regionalmanager');
  });

  it('returns nothing for slots that are missing or not a list', () => {
    for (const value of [undefined, null, 'x', {}, []]) {
      assert.deepEqual(groupSlots(value), [], String(value));
    }
  });
});

describe('isGranted', () => {
  it('reads one slot of a permission', () => {
    const [create] = matrix().permissions;

    assert.equal(isGranted(create, 'company', 'organizationadmin'), true);
    assert.equal(isGranted(create, 'agency', 'organizationadmin'), true);
    assert.equal(isGranted(create, 'company', 'contentwriter'), false);
    assert.equal(isGranted(create, 'creator', 'contentwriter'), false);
  });

  it('is false for a slot that is not in the matrix, and for a permission that is missing', () => {
    const [create] = matrix().permissions;

    assert.equal(isGranted(create, 'creator', 'organizationadmin'), false);
    assert.equal(isGranted(create, 'holding', 'organizationadmin'), false);
    assert.equal(isGranted(undefined, 'company', 'organizationadmin'), false);
    assert.equal(isGranted({}, 'company', 'organizationadmin'), false);
  });

  it('is true only for a grant that is exactly true', () => {
    const permission = { grants: [{ businessModel: 'company', role: 'organizationadmin', granted: 'yes' }] };

    assert.equal(isGranted(permission, 'company', 'organizationadmin'), false);
  });
});

describe('cellLabel', () => {
  it('names the permission, the kind of organization and the role, for someone who cannot see the grid', () => {
    const [create] = matrix().permissions;

    assert.equal(cellLabel(create, 'agency', 'clientmanager'), 'Create workspaces: Marketing agency, Client Manager');
  });
});

describe('withGrant', () => {
  it('changes exactly one cell', () => {
    const before = matrix();

    const after = withGrant(before, 'workspaces.create', 'company', 'contentwriter', true);

    assert.equal(isGranted(after.permissions[0], 'company', 'contentwriter'), true);
    const changed = after.permissions.flatMap((permission, p) =>
      permission.grants.filter((grant, g) => grant.granted !== before.permissions[p].grants[g].granted),
    );
    assert.equal(changed.length, 1);
  });

  it('can take a grant away', () => {
    const after = withGrant(matrix(), 'workspaces.create', 'company', 'organizationadmin', false);

    assert.equal(isGranted(after.permissions[0], 'company', 'organizationadmin'), false);
    assert.equal(isGranted(after.permissions[0], 'agency', 'organizationadmin'), true);
  });

  it('never edits the matrix it was given', () => {
    const before = matrix();
    const snapshot = JSON.stringify(before);

    withGrant(before, 'workspaces.create', 'company', 'contentwriter', true);

    assert.equal(JSON.stringify(before), snapshot);
  });

  it('leaves the matrix as it is when the key or the slot is not in it', () => {
    const before = matrix();

    assert.deepEqual(withGrant(before, 'nope.nothing', 'company', 'organizationadmin', false), before);
    assert.deepEqual(withGrant(before, 'workspaces.create', 'creator', 'organizationadmin', true), before);
  });
});

describe('messageForPermissionError', () => {
  const failure = (status, code, errors = null) => new ApiError(status, 'Detail the page must not show.', { code, errors });

  it('explains a protected permission', () => {
    assert.match(messageForPermissionError(failure(409, 'permission_protected')), /protected/);
  });

  it('explains a role that is not in that kind of organization', () => {
    assert.match(messageForPermissionError(failure(400, 'invalid_slot')), /does not exist/);
  });

  it('says what happened to a site admin who is no longer one, a permission that is gone, and a change made too fast', () => {
    assert.match(messageForPermissionError(failure(403, 'not_site_admin')), /no longer a site admin/);
    assert.match(messageForPermissionError(failure(404, 'permission_not_found')), /no longer exists/);
    assert.match(messageForPermissionError(failure(429, 'rate_limited')), /Too many/);
  });

  it('shows the first field message of a bad request', () => {
    assert.equal(messageForPermissionError(failure(400, 'validation_failed', { role: ['Role is required.'] })), 'Role is required.');
  });

  it('never shows what the server said in its detail', () => {
    for (const status of [400, 404, 409, 500, 503]) {
      assert.doesNotMatch(messageForPermissionError(failure(status, 'something_new')), /Detail the page must not show/, String(status));
    }
  });

  it('says the server could not be reached for anything that is not an API answer', () => {
    assert.match(messageForPermissionError(new TypeError('network down')), /Could not reach the server/);
  });
});

describe('messageForMatrixLoadError', () => {
  it('words the common failures', () => {
    assert.match(messageForMatrixLoadError(new ApiError(403, 'x')), /no longer a site admin/);
    assert.match(messageForMatrixLoadError(new ApiError(429, 'x')), /Too many/);
    assert.equal(messageForMatrixLoadError(new ApiError(500, 'x')), 'The permissions could not be loaded.');
    assert.match(messageForMatrixLoadError(new Error('offline')), /Could not reach the server/);
  });
});
