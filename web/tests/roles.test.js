import assert from 'node:assert/strict';
import { describe, it } from 'node:test';
import {
  abilities,
  allowsSeveralWorkspaces,
  businessModels,
  organizationNameLabel,
  responsibilities,
  roleTitle,
  upcomingAreas,
  workspaceWords,
} from '../js/core/roles.js';

// The table the business asked for. The server enforces it; the page must say the same thing.
const allowed = {
  company: ['organizationadmin', 'marketingmanager', 'contentwriter'],
  agency: ['organizationadmin', 'clientmanager', 'marketingmanager', 'contentwriter'],
  creator: ['contentwriter'],
};
const everyRole = ['organizationadmin', 'clientmanager', 'marketingmanager', 'contentwriter'];

describe('businessModels', () => {
  it('offers the three kinds of organization, with the names the API uses', () => {
    assert.deepEqual(
      businessModels.map((model) => model.value),
      ['company', 'agency', 'creator'],
    );
  });

  it('gives each one a title and a short explanation', () => {
    for (const model of businessModels) {
      assert.ok(model.title.length > 0, model.value);
      assert.ok(model.description.length > 20, model.value);
    }
  });
});

describe('roleTitle', () => {
  it('names the four roles', () => {
    assert.equal(roleTitle('organizationadmin'), 'Organization Admin');
    assert.equal(roleTitle('clientmanager'), 'Client Manager');
    assert.equal(roleTitle('marketingmanager'), 'Marketing Manager');
    assert.equal(roleTitle('contentwriter'), 'Content Writer');
  });

  it('shows a role it does not know as given, and a dash when there is none', () => {
    assert.equal(roleTitle('owner'), 'owner');
    assert.equal(roleTitle('constructor'), 'constructor');
    for (const value of [undefined, null, '', 3]) {
      assert.equal(roleTitle(value), '–', String(value));
    }
  });
});

describe('responsibilities', () => {
  it('has text for every role a business model allows', () => {
    for (const [model, roles] of Object.entries(allowed)) {
      for (const role of roles) {
        assert.ok(responsibilities(model, role)?.length > 20, `${model} / ${role}`);
      }
    }
  });

  it('has nothing for a role the business model does not allow', () => {
    for (const [model, roles] of Object.entries(allowed)) {
      for (const role of everyRole.filter((candidate) => !roles.includes(candidate))) {
        assert.equal(responsibilities(model, role), null, `${model} / ${role}`);
      }
    }
  });

  it('says what each role does, in the words of the brief', () => {
    assert.equal(
      responsibilities('company', 'organizationadmin'),
      'Creates and manages the company workspace, users, permissions and social accounts.',
    );
    assert.equal(
      responsibilities('agency', 'clientmanager'),
      'Manages individual client relationships, requirements, feedback and client approvals.',
    );
    assert.equal(
      responsibilities('creator', 'contentwriter'),
      'Manages content ideas, AI generation, captions, editing, approval and personal publishing.',
    );
  });

  it('gives the same role different duties in a company and in an agency', () => {
    for (const role of ['organizationadmin', 'marketingmanager', 'contentwriter']) {
      assert.notEqual(responsibilities('company', role), responsibilities('agency', role), role);
    }
  });

  it('does not read inherited object properties as models or roles', () => {
    assert.equal(responsibilities('constructor', 'contentwriter'), null);
    assert.equal(responsibilities('company', 'constructor'), null);
    assert.equal(responsibilities(undefined, undefined), null);
  });
});

describe('upcomingAreas', () => {
  it('lists what is still to come for each role, and nothing is shared between roles', () => {
    const titles = everyRole.flatMap((role) => upcomingAreas(role).map((area) => area.title));

    assert.equal(titles.length, everyRole.length * 2);
    assert.equal(new Set(titles).size, titles.length);
    for (const role of everyRole) {
      for (const area of upcomingAreas(role)) {
        assert.ok(area.title.length > 0 && area.text.length > 0, role);
      }
    }
  });

  it('is empty for a role it does not know', () => {
    assert.deepEqual(upcomingAreas('owner'), []);
    assert.deepEqual(upcomingAreas('constructor'), []);
    assert.deepEqual(upcomingAreas(undefined), []);
  });
});

describe('abilities', () => {
  // The permissions each role holds in a fresh installation, as the server seeds them.
  const defaults = {
    organizationadmin: ['dashboard.view', 'workspaces.view', 'workspaces.create', 'data.read', 'data.write', 'organization.manage'],
    clientmanager: ['dashboard.view', 'workspaces.view', 'data.read'],
    marketingmanager: ['dashboard.view', 'workspaces.view', 'data.read', 'data.write'],
    contentwriter: ['dashboard.view', 'workspaces.view', 'data.read', 'data.write'],
  };

  it('reads what the token carries: the three baselines behind CanRead, CanWrite and CanManage', () => {
    assert.deepEqual(abilities(defaults.organizationadmin), { read: true, write: true, manage: true });
    assert.deepEqual(abilities(defaults.clientmanager), { read: true, write: false, manage: false });
    assert.deepEqual(abilities(defaults.marketingmanager), { read: true, write: true, manage: false });
    assert.deepEqual(abilities(defaults.contentwriter), { read: true, write: true, manage: false });
  });

  it('follows the Site admin: a role that was given a permission can, one that lost it cannot', () => {
    assert.equal(abilities([...defaults.contentwriter, 'organization.manage']).manage, true);
    assert.equal(abilities(defaults.organizationadmin.filter((key) => key !== 'data.write')).write, false);
  });

  it('allows nothing when the list is missing or is not a list', () => {
    for (const permissions of [undefined, null, [], 'data.read', {}, 7]) {
      assert.deepEqual(abilities(permissions), { read: false, write: false, manage: false }, String(permissions));
    }
  });

  it('matches a permission exactly', () => {
    assert.deepEqual(abilities(['Data.Read', 'data.read ', 'data', 'data.*']), { read: false, write: false, manage: false });
  });
});

describe('workspaces', () => {
  it('calls them client workspaces in an agency, and your workspace for a creator', () => {
    assert.deepEqual(workspaceWords('agency'), { one: 'client workspace', many: 'Client workspaces' });
    assert.deepEqual(workspaceWords('creator'), { one: 'workspace', many: 'Your workspace' });
    assert.deepEqual(workspaceWords('company'), { one: 'workspace', many: 'Workspaces' });
  });

  it('lets only an individual creator be limited to one', () => {
    assert.equal(allowsSeveralWorkspaces('company'), true);
    assert.equal(allowsSeveralWorkspaces('agency'), true);
    assert.equal(allowsSeveralWorkspaces('creator'), false);
  });
});

describe('organizationNameLabel', () => {
  it('asks for the right name for each kind of organization', () => {
    assert.equal(organizationNameLabel('company'), 'Company name');
    assert.equal(organizationNameLabel('agency'), 'Agency name');
    assert.equal(organizationNameLabel('creator'), 'Your name, brand or channel');
    assert.equal(organizationNameLabel(''), 'Organization name');
    assert.equal(organizationNameLabel(null), 'Organization name');
  });
});
