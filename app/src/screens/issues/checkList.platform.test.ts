// A check whose fix the site's platform owns (Shopify sets its cookies and headers): kept in the list, its severity's
// filter and "Show all", but left out of the headline's things to fix (fix-it pass, the Shopify lane's contract).
import { describe, expect, it } from 'vitest';
import type { Analysis, Check, Checks } from '../../api/types';
import { analysisTotals, buildCheckList, checkEntries, type CheckListFilters } from './checkList';

const check = (id: string, severity: Check['severity']): Check => ({
  id,
  category: 'security',
  severity,
  title: id,
  description: '',
  why: '',
  fix: '',
  learnMore: null,
  grouping: 'none',
});

const CHECKS: Checks = {
  categories: [{ id: 'security', title: 'Security' }],
  checks: [check('cookies-insecure', 'warning'), check('security-headers-missing', 'warning')],
};

const SHOPIFY = { name: 'Shopify', reason: 'Shopify sets these cookies; a store can’t change them.' };

const ANALYSIS: Analysis = {
  analyzedAt: '2026-11-18T10:00:00Z',
  engineVersion: 'test',
  pagesAnalyzed: 10,
  durationMs: 1,
  isPartial: false,
  outOfDate: false,
  outOfDateReasons: [],
  notes: [],
  counts: [
    { checkId: 'cookies-insecure', count: 9, causes: 9, templateCauses: 0, platform: SHOPIFY },
    { checkId: 'security-headers-missing', count: 1, causes: 1, templateCauses: 0, platform: null },
  ],
  skipped: [],
};

const FILTERS: CheckListFilters = { severity: [], q: '', showPassed: false, showHidden: false, hidden: new Set() };

describe('a check the platform owns', () => {
  it('carries who owns it and why, as given', () => {
    const entries = checkEntries(CHECKS, ANALYSIS);
    expect(entries.find((entry) => entry.check.id === 'cookies-insecure')?.platform).toEqual(SHOPIFY);
    expect(entries.find((entry) => entry.check.id === 'security-headers-missing')?.platform).toBeNull();
  });

  it('is left out of the things to fix, and kept in its severity and in the list', () => {
    const entries = checkEntries(CHECKS, ANALYSIS);
    const totals = analysisTotals(entries);
    expect(totals.fixCauses).toBe(1);
    expect(totals.found).toBe(2);
    const warnings = buildCheckList(CHECKS, entries, { ...FILTERS, severity: ['warning'] });
    expect(warnings.visible.map((entry) => entry.check.id)).toContain('cookies-insecure');
  });
});
