import { createMemoryHistory } from '@tanstack/react-router';
import { act, render, renderHook, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { setTransport } from '../api/transport';
import { App } from '../app/App';
import { createQueryClient } from '../app/queryClient';
import { createAppRouter } from '../app/router';
import { getSettings, updateSettings } from '../settings/settingsStore';
import { fakeSidecar, standardRoutes } from '../test/fakeSidecar';
import { NARROW_WINDOW, resetSidebarForTests, toggleSidebar, useSidebarCollapsed } from './sidebarState';

/** A window whose width can change, as matchMedia sees it. */
function windowOfWidth(initial: number) {
  let width = initial;
  const listeners = new Set<() => void>();
  const original = Object.getOwnPropertyDescriptor(window, 'matchMedia');
  window.matchMedia = (query: string) => {
    const max = Number(/max-width:\s*(\d+)px/.exec(query)?.[1] ?? Number.POSITIVE_INFINITY);
    return {
      get matches() {
        return width <= max;
      },
      media: query,
      onchange: null,
      addEventListener: (_type: string, listener: () => void) => listeners.add(listener),
      removeEventListener: (_type: string, listener: () => void) => listeners.delete(listener),
      addListener: () => undefined,
      removeListener: () => undefined,
      dispatchEvent: () => false,
    } as unknown as MediaQueryList;
  };
  return {
    resize: (next: number) =>
      act(() => {
        width = next;
        listeners.forEach((listener) => listener());
      }),
    restore: () => {
      if (original) Object.defineProperty(window, 'matchMedia', original);
    },
  };
}

let screenWidth: ReturnType<typeof windowOfWidth> | null = null;
beforeEach(() => updateSettings({ sidebarCollapsed: false }));
afterEach(() => {
  screenWidth?.restore();
  screenWidth = null;
  resetSidebarForTests();
  updateSettings({ sidebarCollapsed: false });
  setTransport(fakeSidecar([]));
});

describe('the sidebar in a narrow window', () => {
  it('starts collapsed there, opens with the toggle, and never overwrites the wide window’s choice', () => {
    screenWidth = windowOfWidth(640);
    const { result } = renderHook(() => useSidebarCollapsed());
    expect(result.current).toBe(true);
    act(() => toggleSidebar());
    expect(result.current).toBe(false);
    expect(getSettings().sidebarCollapsed).toBe(false);
    act(() => toggleSidebar());
    expect(result.current).toBe(true);
    expect(getSettings().sidebarCollapsed).toBe(false);
    // Wide again: the remembered choice; narrow again: collapsed, as a new narrow window starts.
    act(() => toggleSidebar());
    screenWidth.resize(NARROW_WINDOW + 200);
    expect(result.current).toBe(false);
    screenWidth.resize(640);
    expect(result.current).toBe(true);
  });

  it('keeps the remembered choice in a wide window, where the toggle changes it', () => {
    screenWidth = windowOfWidth(1280);
    const { result } = renderHook(() => useSidebarCollapsed());
    expect(result.current).toBe(false);
    act(() => toggleSidebar());
    expect(result.current).toBe(true);
    expect(getSettings().sidebarCollapsed).toBe(true);
  });

  it('draws the sidebar as icons at 200% zoom on a 1280 px window', async () => {
    screenWidth = windowOfWidth(640);
    setTransport(fakeSidecar(standardRoutes()));
    const queryClient = createQueryClient();
    const router = createAppRouter(queryClient, createMemoryHistory({ initialEntries: ['/'] }));
    render(<App router={router} queryClient={queryClient} />);
    await act(() => router.load());
    expect(await screen.findByRole('button', { name: 'Expand the sidebar' })).toHaveAttribute('aria-expanded', 'false');
  });
});
