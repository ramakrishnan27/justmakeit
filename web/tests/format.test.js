import assert from 'node:assert/strict';
import { describe, it } from 'node:test';
import { businessModelLabel, countLabel, formatDate, formatNumber, pageSummary, planLabel } from '../js/core/format.js';

describe('formatNumber', () => {
  it('groups thousands', () => {
    assert.equal(formatNumber(0), '0');
    assert.equal(formatNumber(999), '999');
    assert.equal(formatNumber(1234567), '1,234,567');
  });

  it('shows a dash for something that is not a number', () => {
    for (const value of [undefined, null, NaN, Infinity, 'x']) {
      assert.equal(formatNumber(value), '–', String(value));
    }
  });
});

describe('formatDate', () => {
  it('formats an ISO date in the given time zone', () => {
    assert.equal(formatDate('2026-10-31T23:30:00+00:00', { timeZone: 'UTC' }), 'Oct 31, 2026');
    assert.equal(formatDate('2026-10-31T23:30:00+00:00', { timeZone: 'Asia/Kolkata' }), 'Nov 1, 2026');
  });

  it('can include the time', () => {
    assert.match(formatDate('2026-10-31T09:05:00Z', { timeZone: 'UTC', withTime: true }), /^Oct 31, 2026,? 9:05\s?AM$/);
  });

  it('accepts a timestamp and a Date', () => {
    const when = Date.UTC(2026, 0, 2, 12);
    assert.equal(formatDate(when, { timeZone: 'UTC' }), 'Jan 2, 2026');
    assert.equal(formatDate(new Date(when), { timeZone: 'UTC' }), 'Jan 2, 2026');
  });

  it('shows a dash for a missing or invalid date', () => {
    for (const value of [undefined, null, '', 'not a date', NaN]) {
      assert.equal(formatDate(value), '–', String(value));
    }
  });
});

describe('planLabel', () => {
  it('capitalises the plan', () => {
    assert.equal(planLabel('trial'), 'Trial');
    assert.equal(planLabel('paid'), 'Paid');
    assert.equal(planLabel('enterprise'), 'Enterprise');
  });

  it('shows a dash when there is no plan', () => {
    assert.equal(planLabel(''), '–');
    assert.equal(planLabel(undefined), '–');
    assert.equal(planLabel(null), '–');
  });
});

describe('pageSummary', () => {
  it('shows the range and the total', () => {
    assert.equal(pageSummary(1, 10, 45), '1–10 of 45');
    assert.equal(pageSummary(5, 10, 45), '41–45 of 45');
    assert.equal(pageSummary(1, 10, 3), '1–3 of 3');
    assert.equal(pageSummary(1, 10, 12345), '1–10 of 12,345');
  });

  it('says so when there is nothing, or the page is past the end', () => {
    assert.equal(pageSummary(1, 10, 0), '0 results');
    assert.equal(pageSummary(1, 10, undefined), '0 results');
    assert.equal(pageSummary(9, 10, 45), '0 of 45');
  });
});

describe('countLabel', () => {
  it('uses the singular for one and the plural otherwise', () => {
    assert.equal(countLabel(1, 'page'), '1 page');
    assert.equal(countLabel(0, 'page'), '0 pages');
    assert.equal(countLabel(2, 'page'), '2 pages');
    assert.equal(countLabel(1500, 'page'), '1,500 pages');
    assert.equal(countLabel(2, 'agency', 'agencies'), '2 agencies');
  });
});

describe('businessModelLabel', () => {
  it('names the three business models', () => {
    assert.equal(businessModelLabel('company'), 'Company');
    assert.equal(businessModelLabel('agency'), 'Marketing agency');
    assert.equal(businessModelLabel('creator'), 'Individual creator');
  });

  it('shows a model it does not know as given, capitalised', () => {
    assert.equal(businessModelLabel('franchise'), 'Franchise');
  });

  it('does not read inherited object properties as models', () => {
    assert.equal(businessModelLabel('constructor'), 'Constructor');
    assert.equal(businessModelLabel('__proto__'), '__proto__');
  });

  it('shows a dash when there is no model', () => {
    for (const value of [undefined, null, '', 3, {}]) {
      assert.equal(businessModelLabel(value), '–', String(value));
    }
  });
});
