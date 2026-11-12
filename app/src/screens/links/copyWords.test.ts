import { describe, expect, it } from 'vitest';
import { copiedPartOfEnds, copiesPart, copyColumnTitle, copyEndsTitle } from './copyWords';

describe('what Links’ copies are called', () => {
  it('is a part of the view only past the most a copy reads', () => {
    expect(copiesPart(null)).toBe(false);
    expect(copiesPart(0)).toBe(false);
    expect(copiesPart(10_000)).toBe(false);
    expect(copiesPart(10_001)).toBe(true);
    expect(copiesPart(583_554)).toBe(true);
  });

  it('names the view’s URLs when the copy reads every link', () => {
    expect(copyEndsTitle('to', false)).toBe('Copy the target URLs');
    expect(copyEndsTitle('from', false)).toBe('Copy the source URLs');
    expect(copyColumnTitle('Kind', false)).toBe('Copy the Kind column');
    expect(copyColumnTitle(undefined, false)).toBe('Copy a column');
  });

  it('says which links they are of when the view has more than a copy reads', () => {
    expect(copyEndsTitle('to', true)).toBe('Copy the first 10,000 links’ target URLs');
    expect(copyEndsTitle('from', true)).toBe('Copy the first 10,000 links’ source URLs');
    expect(copyColumnTitle('Kind', true)).toBe('Copy the Kind column of the first 10,000 links');
  });

  it('says whose URLs were copied, how many, and how many links the view has', () => {
    // The review's case: 583,554 links sorted by the target's status, whose first 10,000 all lead to one URL.
    expect(copiedPartOfEnds('to', 10_000, 583_554, 1)).toEqual({
      title: 'Copied the first 10,000 links’ target URLs',
      description: '1 URL, each once. The view has 583,554 links: a copy takes no more than 10,000.',
    });
    expect(copiedPartOfEnds('from', 10_000, 5_053_909, 2_417)).toEqual({
      title: 'Copied the first 10,000 links’ source URLs',
      description: '2,417 URLs, each once. The view has 5,053,909 links: a copy takes no more than 10,000.',
    });
  });
});
