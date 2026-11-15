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
    expect(shown).toMatch(/<head><style id="si-app-scroll-bars">[\s\S]*<\/style><title>R<\/title><\/head>/);
    const style = scrollBarStyle(LOOK);
    expect(style).toContain('@media not (forced-colors: active)');
    expect(style).toContain('::-webkit-scrollbar { width: 10px; height: 10px; }');
    expect(style).toContain('background: rgb(138, 138, 147) padding-box');
    expect(style).not.toMatch(/scrollbar-(width|color)/);
  });

  it('puts them ahead of the report’s own policy, which allows only the style sheet it names by hash', () => {
    // The engine's head, as ReportDocument writes it: a policy in a meta element governs only what comes after it.
    const engine = [
      '<!doctype html>\n<html lang="en-GB">\n<head>\n<meta charset="utf-8">\n',
      `<meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'sha256-GhS/vd7B+cvHWvPCS5kBNhwZSIHUDxukZyPlDpda3aY='; base-uri 'none'; form-action 'none'">\n`,
      '<title>Website check</title>\n<style>body{margin:0}</style>\n</head>\n<body>x</body></html>',
    ].join('');
    const shown = withAppScrollBars(engine, LOOK);
    const bars = shown.indexOf(`<style id="${SCROLL_BARS_STYLE_ID}">`);
    expect(bars).toBeGreaterThan(shown.indexOf('<head>'));
    expect(bars).toBeLessThan(shown.indexOf('http-equiv="Content-Security-Policy"'));
    // The engine's document is otherwise as it was.
    expect(shown.replace(/<style id="si-app-scroll-bars">[\s\S]*?<\/style>/, '')).toBe(engine);
  });

  it('puts them first in a document without a head', () => {
    expect(withAppScrollBars('<p>x</p>', LOOK)).toMatch(new RegExp(`^<style id="${SCROLL_BARS_STYLE_ID}">`));
  });

  it("shows the document as it is when the app's tokens can't be read", () => {
    expect(withAppScrollBars('<p>x</p>', null)).toBe('<p>x</p>');
  });
});
