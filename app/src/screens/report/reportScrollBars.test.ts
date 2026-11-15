import { describe, expect, it } from 'vitest';
import { SCROLL_BARS_STYLE_ID, scrollBarStyle, withAppScrollBars } from './reportScrollBars';

const LOOK = {
  size: '10px',
  inset: '2px',
  thumbMin: '24px',
  thumb: 'rgb(138, 138, 147)',
  thumbHover: 'rgb(104, 104, 111)',
};

describe("the report frame's scroll bars (G6)", () => {
  it("puts the app's bars in the document's head, light, and leaves forced colours to the system", () => {
    const shown = withAppScrollBars('<!doctype html><html><head><title>R</title></head><body>x</body></html>', LOOK);
    expect(shown).toMatch(/<title>R<\/title><style id="si-app-scroll-bars">[\s\S]*<\/style><\/head>/);
    const style = scrollBarStyle(LOOK);
    expect(style).toContain('@media not (forced-colors: active)');
    expect(style).toContain('::-webkit-scrollbar { width: 10px; height: 10px; }');
    expect(style).toContain('background: rgb(138, 138, 147) padding-box');
    expect(style).not.toMatch(/scrollbar-(width|color)/);
  });

  it('puts them first in a document without a head', () => {
    expect(withAppScrollBars('<p>x</p>', LOOK)).toMatch(new RegExp(`^<style id="${SCROLL_BARS_STYLE_ID}">`));
  });

  it("shows the document as it is when the app's tokens can't be read", () => {
    expect(withAppScrollBars('<p>x</p>', null)).toBe('<p>x</p>');
  });
});
