// How a grouped check's rows are shown when it's opened (the 2026-11-19 UX round, A-06): a list, unless grouping
// saves work; and what its grouped view is called on its button.
import { describe, expect, it } from 'vitest';
import type { Check } from '../../api/types';
import { defaultView, groupedLabel } from './checkView';

const check = (id: string, grouping: Check['grouping']): Check => ({
  id,
  category: 'links',
  severity: 'warning',
  title: id,
  description: '',
  why: '',
  fix: '',
  learnMore: null,
  grouping,
});

describe('the view a grouped check opens in', () => {
  it('is its list where each row is a fault of its own', () => {
    expect(defaultView({ check: check('internal-client-error', 'detail'), templateCauses: null })).toBe('list');
    expect(defaultView({ check: check('empty-anchor-text', 'relatedUrl'), templateCauses: 0 })).toBe('list');
    expect(defaultView({ check: check('title-duplicate', 'relatedUrl'), templateCauses: null })).toBe('list');
  });

  it('is its groups for an image check grouped by image: one file is fixed for every page that shows it', () => {
    expect(defaultView({ check: check('image-too-large', 'relatedUrl'), templateCauses: null })).toBe('groups');
    expect(defaultView({ check: check('image-broken', 'relatedUrl'), templateCauses: 0 })).toBe('groups');
    // Not an image check grouped by its detail, nor a check that only has "image" in its name.
    expect(defaultView({ check: check('image-missing-alt', 'detail'), templateCauses: null })).toBe('list');
    expect(defaultView({ check: check('social-image-missing', 'relatedUrl'), templateCauses: null })).toBe('list');
  });

  it('is its groups where the analysis counted a cause in the site’s template: one fix for its pages', () => {
    expect(defaultView({ check: check('empty-anchor-text', 'relatedUrl'), templateCauses: 1 })).toBe('groups');
    expect(defaultView({ check: check('internal-client-error', 'detail'), templateCauses: 3 })).toBe('groups');
  });
});

describe('what the grouped view is called', () => {
  it('says what its groups are by', () => {
    expect(groupedLabel(check('internal-client-error', 'detail'))).toBe('Grouped by cause');
    expect(groupedLabel(check('image-too-large', 'relatedUrl'))).toBe('Grouped by image');
    expect(groupedLabel(check('title-duplicate', 'relatedUrl'))).toBe('Grouped by what they share');
    expect(groupedLabel(check('description-duplicate', 'relatedUrl'))).toBe('Grouped by what they share');
    expect(groupedLabel(check('empty-anchor-text', 'relatedUrl'))).toBe('Grouped by linked URL');
    expect(groupedLabel(check('links-to-redirects', 'relatedUrl'))).toBe('Grouped by linked URL');
  });

  it('says “by cause” for an image check grouped by its detail', () => {
    expect(groupedLabel(check('image-missing-alt', 'detail'))).toBe('Grouped by cause');
  });
});
