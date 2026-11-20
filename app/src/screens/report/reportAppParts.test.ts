import { describe, expect, it, vi } from 'vitest';
import {
  APP_PARTS_STYLE_ID,
  fitZoom,
  listenToReportLinks,
  reportLinkOf,
  withAppParts,
  type ReportLinkHandlers,
} from './reportAppParts';

// The engine's report, as far as the app's parts care: its policy in the head, a problem's heading with the comment
// where the app's link goes, a row with a link out, and "Fix these first" leading to the section.
const REPORT = [
  '<!doctype html>',
  '<html lang="en-GB">',
  '<head>',
  '<meta charset="utf-8">',
  '<meta http-equiv="Content-Security-Policy" content="default-src \'none\'; style-src \'sha256-abc\'">',
  '<style>a { color: inherit; }</style>',
  '</head>',
  '<body>',
  '<ol class="first"><li><a class="first__title" href="#problem-internal-client-error">Pages or files that can’t be found</a></li></ol>',
  '<article class="problem" id="problem-internal-client-error" data-check="internal-client-error">',
  '<h3><span>1. Pages or files that can’t be found</span><!--si:open internal-client-error--></h3>',
  '<table><tbody><tr><td><span class="mono"><a href="https://example.com/blog/" target="_blank" rel="noopener noreferrer nofollow"><b>/blog/</b></a></span></td></tr></tbody></table>',
  '</article>',
  '<article class="problem" id="problem-title-too-long" data-check="title-too-long">',
  '<h3><span>2. Titles that may be cut off</span><!--si:open title-too-long--><span class="problem__tech">Title too long · Notice</span></h3>',
  '</article>',
  '</body>',
  '</html>',
].join('\n');

function documentOf(html: string): Document {
  return new DOMParser().parseFromString(html, 'text/html');
}

describe('the app’s own parts in the report’s frame', () => {
  it('puts a link into each problem where the engine left room for one, named for where it leads', () => {
    const shown = documentOf(withAppParts(REPORT, 'Open in What to fix'));
    const links = [...shown.querySelectorAll('a.si-open')];
    expect(
      links.map((link) => [link.textContent, link.getAttribute('data-si-open'), link.getAttribute('href')]),
    ).toEqual([
      ['Open in What to fix', 'internal-client-error', '#problem-internal-client-error'],
      ['Open in What to fix', 'title-too-long', '#problem-title-too-long'],
    ]);
    // Beside the heading's words, before the technical name where there is one.
    expect(links[0]?.previousElementSibling?.textContent).toBe('1. Pages or files that can’t be found');
    expect(links[1]?.nextElementSibling?.className).toBe('problem__tech');
    expect(withAppParts(REPORT, 'Open in Issues')).toContain('>Open in Issues</a>');
    expect(withAppParts(REPORT, 'Open in What to fix')).not.toContain('<!--si:open');
  });

  it('adds its style ahead of the report’s own policy, which would refuse it after, and hides its links on paper', () => {
    const shown = withAppParts(REPORT, 'Open in What to fix');
    const style = shown.indexOf(`<style id="${APP_PARTS_STYLE_ID}">`);
    expect(style).toBeGreaterThan(shown.indexOf('<head>'));
    expect(style).toBeLessThan(shown.indexOf('Content-Security-Policy'));
    const sheet = documentOf(shown).getElementById(APP_PARTS_STYLE_ID)?.textContent ?? '';
    expect(sheet).toMatch(/@media print \{[^}]*\.si-open \{ display: none; \}/);
    // The preview's zoom is the screen's alone: paper is the report's own size.
    expect(sheet).toMatch(/@media screen \{[^}]*body \{ zoom: var\(--si-zoom, 1\); \}/);
  });

  it('leaves a document with no room for links as it is, but for the style', () => {
    const plain =
      '<!doctype html><html><head><title>Website check</title></head><body><p>The summary.</p></body></html>';
    const shown = withAppParts(plain, 'Open in Issues');
    expect(shown).not.toContain('si-open"');
    expect(documentOf(shown).body.innerHTML).toBe('<p>The summary.</p>');
  });

  it('never takes a check’s id that isn’t one', () => {
    // What a site sends is escaped by the engine, so it can't be a comment; an id that isn't a check's is left alone.
    const odd = REPORT.replace('<!--si:open title-too-long-->', '<!--si:open "><script>x</script>-->');
    expect(documentOf(withAppParts(odd, 'Open in Issues')).querySelectorAll('a.si-open')).toHaveLength(1);
    expect(documentOf(withAppParts(odd, 'Open in Issues')).querySelector('script')).toBeNull();
  });
});

describe('a click in the report', () => {
  const shown = () => documentOf(withAppParts(REPORT, 'Open in What to fix'));

  it('is read as what the link is: a problem in the app, a part of the report, or an address outside', () => {
    const document = shown();
    expect(reportLinkOf(document.querySelector('a.si-open'))).toEqual({
      kind: 'check',
      checkId: 'internal-client-error',
    });
    expect(reportLinkOf(document.querySelector('a.first__title'))).toEqual({
      kind: 'part',
      id: 'problem-internal-client-error',
    });
    // The click lands on what's inside the link.
    expect(reportLinkOf(document.querySelector('td b'))).toEqual({ kind: 'url', url: 'https://example.com/blog/' });
    expect(reportLinkOf(document.querySelector('h3 > span'))).toBeNull();
    expect(reportLinkOf(null)).toBeNull();
    const other = documentOf(
      '<a id="mail" href="mailto:a@example.com">a</a><a id="script" href="javascript:alert(1)">b</a>',
    );
    expect(reportLinkOf(other.getElementById('mail'))).toBeNull();
    expect(reportLinkOf(other.getElementById('script'))).toBeNull();
  });

  function listening(document: Document): ReportLinkHandlers & { stop: () => void } {
    const handlers = { onOpenUrl: vi.fn(), onOpenCheck: vi.fn() };
    return { ...handlers, stop: listenToReportLinks(document, handlers) };
  }

  const click = (target: Element | null) => {
    const event = new MouseEvent('click', { bubbles: true, cancelable: true });
    target?.dispatchEvent(event);
    return event;
  };

  it('opens an outside address in the browser, never in the frame', () => {
    const document = shown();
    const handlers = listening(document);
    const event = click(document.querySelector('td b'));
    expect(handlers.onOpenUrl).toHaveBeenCalledExactlyOnceWith('https://example.com/blog/');
    expect(handlers.onOpenCheck).not.toHaveBeenCalled();
    expect(event.defaultPrevented).toBe(true);
  });

  it('leads from a problem’s heading to that problem in the app', () => {
    const document = shown();
    const handlers = listening(document);
    const event = click(document.querySelector('a.si-open'));
    expect(handlers.onOpenCheck).toHaveBeenCalledExactlyOnceWith('internal-client-error');
    expect(handlers.onOpenUrl).not.toHaveBeenCalled();
    expect(event.defaultPrevented).toBe(true);
  });

  it('scrolls the report to the part a link inside it names', () => {
    const document = shown();
    const handlers = listening(document);
    const section = document.getElementById('problem-internal-client-error');
    const scrollIntoView = vi.fn();
    if (section) section.scrollIntoView = scrollIntoView;
    const event = click(document.querySelector('a.first__title'));
    expect(scrollIntoView).toHaveBeenCalledTimes(1);
    expect(event.defaultPrevented).toBe(true);
    expect(handlers.onOpenUrl).not.toHaveBeenCalled();
    expect(handlers.onOpenCheck).not.toHaveBeenCalled();
  });

  it('leaves a click on anything else alone, and stops listening when told', () => {
    const document = shown();
    const handlers = listening(document);
    expect(click(document.querySelector('h3 > span')).defaultPrevented).toBe(false);
    handlers.stop();
    click(document.querySelector('td b'));
    expect(handlers.onOpenUrl).not.toHaveBeenCalled();
  });

  it('opens an outside address on a middle click too', () => {
    const document = shown();
    const handlers = listening(document);
    document
      .querySelector('td b')
      ?.dispatchEvent(new MouseEvent('auxclick', { bubbles: true, cancelable: true, button: 1 }));
    expect(handlers.onOpenUrl).toHaveBeenCalledExactlyOnceWith('https://example.com/blog/');
  });
});

describe('the preview’s zoom', () => {
  it('fits the paper to the pane, never smaller than it prints and never past half as big again', () => {
    expect(fitZoom(860, 860)).toBe(1);
    expect(fitZoom(600, 860)).toBe(1);
    expect(fitZoom(1075, 860)).toBe(1.25);
    expect(fitZoom(2400, 860)).toBe(1.5);
    expect(fitZoom(0, 860)).toBe(1);
    expect(fitZoom(1000, 0)).toBe(1);
  });

  it('moves in steps a twentieth wide, so a pane dragged a pixel doesn’t redraw the sheet', () => {
    expect(fitZoom(1000, 860)).toBe(1.15);
    expect(fitZoom(1001, 860)).toBe(1.15);
  });
});
