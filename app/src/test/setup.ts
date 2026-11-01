// jsdom lacks a few browser APIs the app uses; these stand-ins are enough for unit and component tests.
import '@testing-library/jest-dom/vitest';
import { cleanup } from '@testing-library/react';
import { afterEach, vi } from 'vitest';

afterEach(() => cleanup());

if (typeof window.matchMedia !== 'function') {
  vi.stubGlobal('matchMedia', (query: string) => ({
    matches: false,
    media: query,
    onchange: null,
    addEventListener: () => undefined,
    removeEventListener: () => undefined,
    addListener: () => undefined,
    removeListener: () => undefined,
    dispatchEvent: () => false,
  }));
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
