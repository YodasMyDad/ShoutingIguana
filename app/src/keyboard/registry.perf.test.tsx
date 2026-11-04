// What the registry costs the controls that read it: grids put buttons and URLs in their cells, hundreds at a time.
import { act, render } from '@testing-library/react';
import { Profiler } from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { Button } from '../ui/Button';
import { Url } from '../ui/Url';
import { commands, type Command } from './registry';

const many = (count: number): Command[] =>
  Array.from({ length: count }, (_, index) => ({
    id: `test.c${index}`,
    title: `Command ${index}`,
    group: 'App',
    keys: [`mod+${index % 10}`],
    run: () => undefined,
  }));

let unregister: () => void = () => undefined;
afterEach(() => {
  unregister();
  vi.restoreAllMocks();
});

/** A change to the registry that touches none of the controls' commands, as a screen's commands coming and going. */
const unrelatedChange = () =>
  act(() => {
    commands.register([{ id: 'test.other', title: 'Other', group: 'App', run: () => undefined }])();
  });

describe('the registry, for the controls in a grid', () => {
  it('ranks its commands once per change, however many lookups there are', () => {
    unregister = commands.register(many(40));
    const sort = vi.spyOn(Array.prototype, 'sort');
    for (let index = 0; index < 100; index++) commands.get(`test.c${index % 40}`);
    expect(sort.mock.calls.length).toBeLessThanOrEqual(1);
  });

  it('does no lookups for a URL or a button without a command, and doesn’t render them again', () => {
    unregister = commands.register(many(40));
    const get = vi.spyOn(commands, 'get');
    const keysOf = vi.spyOn(commands, 'keysOf');
    let commits = 0;
    render(
      <Profiler id="cells" onRender={() => commits++}>
        {Array.from({ length: 100 }, (_, index) => (
          <Url key={index} url={`https://www.example.com/page-${index}/`} />
        ))}
        <Button>Export</Button>
      </Profiler>,
    );
    expect(get).not.toHaveBeenCalled();
    expect(keysOf).not.toHaveBeenCalled();
    commits = 0;
    unrelatedChange();
    expect(get).not.toHaveBeenCalled();
    expect(commits).toBe(0);
  });

  it('renders a button with a command again only when its keys change', () => {
    unregister = commands.register(many(40));
    let commits = 0;
    render(
      <Profiler id="cells" onRender={() => commits++}>
        {Array.from({ length: 20 }, (_, index) => (
          <Button key={index} command={`test.c${index}`}>
            Run
          </Button>
        ))}
      </Profiler>,
    );
    commits = 0;
    unrelatedChange();
    expect(commits).toBe(0);
    act(() => {
      unregister();
      unregister = commands.register(many(40).map((command) => ({ ...command, keys: ['f9'] })));
    });
    expect(commits).toBeGreaterThan(0);
  });
});
