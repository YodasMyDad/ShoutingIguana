// jsdom lacks a few browser APIs the app uses; these stand-ins are enough for unit and component tests. A test that
// runs in plain Node (`@vitest-environment node`, such as the compiled tokens') has no DOM, and needs none of them.
import '@testing-library/jest-dom/vitest';
import { cleanup } from '@testing-library/react';
import { afterEach, vi } from 'vitest';

const hasDom = typeof window !== 'undefined';

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
    vi.stubGlobal('ResizeObserver', ResizeObserverStub);
  }

  if (typeof Element.prototype.scrollIntoView !== 'function') {
    Element.prototype.scrollIntoView = () => undefined;
  }
}
