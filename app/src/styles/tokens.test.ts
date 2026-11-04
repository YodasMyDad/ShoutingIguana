// @vitest-environment node
// The tokens as Tailwind compiles them, the way @tailwindcss/vite does for the app: what a component can write and
// what it gets. Rule 2 of docs/v2-ui-architecture.md is "tokens only"; these keep the theme from quietly growing
// Tailwind's own scales back.
import { compile } from '@tailwindcss/node';
import { readFileSync } from 'node:fs';
import path from 'node:path';
import { beforeAll, describe, expect, it } from 'vitest';

const entry = path.resolve(import.meta.dirname, 'index.css');
let build: (candidates: string[]) => string;

beforeAll(async () => {
  const compiler = await compile(readFileSync(entry, 'utf8'), {
    base: path.dirname(entry),
    from: entry,
    onDependency: () => undefined,
  });
  build = (candidates) => compiler.build(candidates);
});

/** The rule a single class compiles to, or '' when it's no class. */
const rule = (candidate: string) => {
  const css = build([candidate]);
  const escaped = candidate.replace(/[.:/()[\]]/g, (char) => `\\${char}`);
  const start = css.indexOf(`.${escaped}`);
  return start < 0 ? '' : css.slice(start, css.indexOf('}', start) + 1);
};

describe('the tokens, compiled', () => {
  it('have a closed spacing scale: clarity’s steps, and nothing between them', () => {
    expect(rule('p-2.5')).toContain('var(--spacing-2_5)');
    expect(rule('gap-0.5')).toContain('var(--spacing-0_5)');
    expect(rule('size-7')).toContain('var(--spacing-7)');
    expect(rule('p-13')).toBe('');
    expect(rule('w-48')).toBe('');
    expect(rule('gap-4.5')).toBe('');
  });

  it('take a transition’s duration and easing from the motion tokens', () => {
    const css = build(['transition-colors']);
    expect(css).toMatch(/--default-transition-duration:\s*var\(--duration-quick\)/);
    expect(css).toMatch(/--default-transition-timing-function:\s*var\(--ease-out\)/);
  });

  it('keep the type features out of the font families', () => {
    expect(rule('font-features')).toBe('');
    expect(rule('font-features-figures')).toBe('');
    expect(rule('font-sans')).toContain('font-family');
  });

  it('mean what they say by narrow: below 1360 px', () => {
    const css = build(['narrow:hidden']);
    expect(css).toMatch(/@media \(max-width: 1360px\)/);
    expect(css).not.toMatch(/min-width: 1360px/);
  });

  it('have no colours or scales but the app’s own', () => {
    for (const candidate of ['bg-blue-500', 'text-sm', 'rounded-2xl', 'shadow-lg', 'text-red-600']) {
      expect(rule(candidate)).toBe('');
    }
    expect(rule('bg-surface')).toContain('var(--color-surface)');
  });
});
