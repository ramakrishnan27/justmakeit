import { describe, expect, it } from 'vitest';
import { adminApp, appForPath, memberApp } from './apps';

describe('appForPath', () => {
  it('gives the Site admin app /admin and everything under it, with or without a trailing slash', () => {
    for (const path of ['/admin', '/admin/', '/admin/login', '/admin/menus', '/admin/anything/else']) {
      expect(appForPath(path), path).toBe(adminApp);
    }
  });

  it('gives the member app everything else, including addresses that only start with the letters', () => {
    for (const path of ['/', '/login', '/register', '/dashboard', '/workspaces', '/administrator', '/adminx/login', '/x/admin']) {
      expect(appForPath(path), path).toBe(memberApp);
    }
  });
});

describe('the two apps', () => {
  it('have their own addresses and names', () => {
    expect(memberApp).toMatchObject({ kind: 'member', label: 'Platform', homePath: '/dashboard', loginPath: '/login' });
    expect(adminApp).toMatchObject({ kind: 'admin', label: 'Site admin', homePath: '/admin', loginPath: '/admin/login' });
  });

  it('list the pages behind sign-in, and each lands on one of them', () => {
    expect(memberApp.pagePaths).toEqual(['/dashboard', '/workspaces']);
    expect(adminApp.pagePaths).toEqual(['/admin']);
    expect(memberApp.pagePaths).toContain(memberApp.homePath);
    expect(adminApp.pagePaths).toContain(adminApp.homePath);
  });

  it('keep the sign-in page out of the pages behind sign-in', () => {
    expect(memberApp.pagePaths).not.toContain(memberApp.loginPath);
    expect(adminApp.pagePaths).not.toContain(adminApp.loginPath);
  });
});
