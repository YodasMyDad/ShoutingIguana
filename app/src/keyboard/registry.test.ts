import { describe, expect, it, vi } from 'vitest';
import { CommandRegistry, type Command } from './registry';

function keydown(key: string, init: KeyboardEventInit = {}, target: EventTarget = document.body) {
  const event = new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true, ...init });
  Object.defineProperty(event, 'target', { value: target });
  return event;
}

const command = (overrides: Partial<Command> & Pick<Command, 'id'>): Command => ({
  title: overrides.id,
  group: 'App',
  run: vi.fn(),
  ...overrides,
});

describe('the command registry', () => {
  it('runs the command whose key was pressed, and takes the key', () => {
    const registry = new CommandRegistry(false);
    const palette = command({ id: 'palette', keys: ['mod+k'] });
    registry.register([palette]);
    const event = keydown('k', { ctrlKey: true });
    expect(registry.handleKeyDown(event)).toBe(true);
    expect(palette.run).toHaveBeenCalledOnce();
    expect(event.defaultPrevented).toBe(true);
  });

  it('uses Cmd on macOS', () => {
    const registry = new CommandRegistry(true);
    const palette = command({ id: 'palette', keys: ['mod+k'] });
    registry.register([palette]);
    expect(registry.handleKeyDown(keydown('k', { ctrlKey: true }))).toBe(false);
    expect(registry.handleKeyDown(keydown('k', { metaKey: true }))).toBe(true);
    expect(registry.shortcut('palette')).toBe('⌘K');
  });

  it('lets a screen’s command replace the app’s, until the screen goes away', () => {
    const registry = new CommandRegistry(false);
    const appEscape = command({ id: 'close', keys: ['escape'] });
    const screenEscape = command({ id: 'close', keys: ['escape'] });
    registry.register([appEscape]);
    const unregister = registry.register([screenEscape]);
    registry.handleKeyDown(keydown('Escape'));
    expect(screenEscape.run).toHaveBeenCalledOnce();
    expect(appEscape.run).not.toHaveBeenCalled();
    unregister();
    registry.handleKeyDown(keydown('Escape'));
    expect(appEscape.run).toHaveBeenCalledOnce();
  });

  it('does nothing for a disabled command', () => {
    const registry = new CommandRegistry(false);
    const pause = command({ id: 'pause', keys: ['f7'], enabled: () => false });
    registry.register([pause]);
    const event = keydown('F7');
    expect(registry.handleKeyDown(event)).toBe(false);
    expect(event.defaultPrevented).toBe(false);
    expect(registry.run('pause')).toBe(false);
  });

  it('never fires a typing key in a text field, and other keys only when allowed there', () => {
    const registry = new CommandRegistry(false);
    const sheet = command({ id: 'sheet', keys: ['?'] });
    const newCrawl = command({ id: 'new', keys: ['mod+n'] });
    const palette = command({ id: 'palette', keys: ['mod+k'], inInputs: true });
    registry.register([sheet, newCrawl, palette]);
    const input = document.createElement('input');
    expect(registry.handleKeyDown(keydown('?', { shiftKey: true }, input))).toBe(false);
    expect(registry.handleKeyDown(keydown('n', { ctrlKey: true }, input))).toBe(false);
    expect(registry.handleKeyDown(keydown('k', { ctrlKey: true }, input))).toBe(true);
    expect(sheet.run).not.toHaveBeenCalled();
    expect(palette.run).toHaveBeenCalledOnce();
  });

  it('leaves a key that something else already handled', () => {
    const registry = new CommandRegistry(false);
    const copyAll = command({ id: 'all', keys: ['mod+a'] });
    registry.register([copyAll]);
    const event = keydown('a', { ctrlKey: true });
    event.preventDefault();
    expect(registry.handleKeyDown(event)).toBe(false);
    expect(copyAll.run).not.toHaveBeenCalled();
  });

  it('only lists grid keys: the focused grid handles them', () => {
    const registry = new CommandRegistry(false);
    const gridCopy = command({ id: 'grid.copy', keys: ['mod+c'], scope: 'grid' });
    registry.register([gridCopy]);
    expect(registry.handleKeyDown(keydown('c', { ctrlKey: true }))).toBe(false);
    expect(registry.list().map((entry) => entry.id)).toEqual(['grid.copy']);
  });

  it('feeds menus, tooltips and the sheet from one place', () => {
    const registry = new CommandRegistry(false);
    registry.register([command({ id: 'zoom', keys: ['mod+=', 'mod++'] })]);
    expect(registry.shortcut('zoom')).toBe('Ctrl+=');
    expect(registry.ariaShortcut('zoom')).toBe('Control+= Control++');
    expect(registry.shortcut('missing')).toBeNull();
  });

  it('reports a failing command instead of throwing from the key handler', async () => {
    const registry = new CommandRegistry(false);
    const onError = vi.fn();
    registry.setErrorHandler(onError);
    registry.register([command({ id: 'boom', keys: ['f9'], run: () => Promise.reject(new Error('nope')) })]);
    registry.handleKeyDown(keydown('F9'));
    await vi.waitFor(() => expect(onError).toHaveBeenCalledOnce());
  });

  it('notifies subscribers when commands change', () => {
    const registry = new CommandRegistry(false);
    const listener = vi.fn();
    registry.subscribe(listener);
    const unregister = registry.register([command({ id: 'x' })]);
    unregister();
    expect(listener).toHaveBeenCalledTimes(2);
  });
});
