// jsdom lacks a few browser APIs the app uses; these stand-ins are enough for unit and component tests. A test that
// runs in plain Node (`@vitest-environment node`, such as the compiled tokens') has no DOM, and needs none of them.
import '@testing-library/jest-dom/vitest';
import { cleanup } from '@testing-library/react';
import { afterEach, beforeEach } from 'vitest';

const hasDom = typeof window !== 'undefined';

// Every test starts in Advanced, which every screen built before Simple was written for; a test of Simple says so.
// Imported here, not above: after the test file's own module mocks are in place (the settings store reaches the shell).
beforeEach(async () => {
  const { replaceSettings } = await import('../settings/settingsStore');
  replaceSettings({ mode: 'advanced' });
  // Every test starts as a new run of the app: no proxy password or header value kept from the one before.
  const { resetSessionSecretsForTests } = await import('../crawl/secrets');
  resetSessionSecretsForTests();
});

if (hasDom) {
  afterEach(() => cleanup());

  // Defined outright rather than stubbed: `unstubGlobals` puts stubs back after every test, and the toaster asks for
  // the OS theme in every test that renders the app.
  if (typeof window.matchMedia !== 'function') {
    Object.defineProperty(window, 'matchMedia', {
      configurable: true,
      writable: true,
      value: (query: string) => ({
        matches: false,
        media: query,
        onchange: null,
        addEventListener: () => undefined,
        removeEventListener: () => undefined,
        addListener: () => undefined,
        removeListener: () => undefined,
        dispatchEvent: () => false,
      }),
    });
  }

  if (typeof window.ResizeObserver !== 'function') {
    class ResizeObserverStub {
      observe(): void {
        // jsdom does no layout, so nothing ever resizes.
      }
      unobserve(): void {
        // Nothing to stop observing.
      }
      disconnect(): void {
        // Nothing to disconnect.
      }
    }
    // Defined outright, as matchMedia is: a stub is put back after a file's first test (`unstubGlobals`), and Radix's
    // checkbox and switch ask for it without checking, so a popover with one in it then threw in every later test.
    Object.defineProperty(window, 'ResizeObserver', { configurable: true, writable: true, value: ResizeObserverStub });
  }

  if (typeof Element.prototype.scrollIntoView !== 'function') {
    Element.prototype.scrollIntoView = () => undefined;
  }
}
