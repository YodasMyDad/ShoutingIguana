import { act, render, screen } from '@testing-library/react';
import { Component, type ReactNode } from 'react';
import { describe, expect, it, vi } from 'vitest';
import { chunk, useChunk, type Chunk } from './chunk';

const Big = () => <p>the big part</p>;

function Shell({ source, wanted }: { source: Chunk; wanted: boolean }) {
  return <div data-testid="shell">{useChunk(source, wanted)}</div>;
}

class Boundary extends Component<{ children: ReactNode }, { failed: string | null }> {
  override state: { failed: string | null } = { failed: null };
  static getDerivedStateFromError(error: unknown) {
    return { failed: error instanceof Error ? error.message : 'failed' };
  }
  override render() {
    return this.state.failed === null ? this.props.children : <p role="alert">{this.state.failed}</p>;
  }
}

/** A load the test settles by hand. */
function deferred() {
  let resolve: (component: typeof Big) => void = () => undefined;
  let reject: (error: unknown) => void = () => undefined;
  const promise = new Promise<typeof Big>((yes, no) => {
    resolve = yes;
    reject = no;
  });
  return { promise, resolve, reject };
}

describe('a component in a chunk of its own', () => {
  it('is in the page in the very render that first wants it, once it’s loaded: nothing is held back', async () => {
    const source = chunk(() => Promise.resolve(Big));
    await source.load();
    const { rerender } = render(<Shell source={source} wanted={false} />);
    expect(screen.queryByText('the big part')).toBeNull();
    // No waiting, no act over a promise: the render that wants it shows it.
    rerender(<Shell source={source} wanted />);
    expect(screen.getByText('the big part')).toBeInTheDocument();
    rerender(<Shell source={source} wanted={false} />);
    expect(screen.queryByText('the big part')).toBeNull();
  });

  it('is fetched when it’s wanted before it was loaded, and shown once it arrives', async () => {
    const load = deferred();
    const importer = vi.fn(() => load.promise);
    const source = chunk(importer);
    render(<Shell source={source} wanted />);
    expect(importer).toHaveBeenCalledOnce();
    expect(screen.getByTestId('shell')).toBeEmptyDOMElement();
    await act(async () => {
      load.resolve(Big);
      await load.promise;
    });
    expect(screen.getByText('the big part')).toBeInTheDocument();
  });

  it('is loaded once, however often it’s asked for', async () => {
    const importer = vi.fn(() => Promise.resolve(Big));
    const source = chunk(importer);
    expect(source.loaded()).toBeNull();
    await Promise.all([source.load(), source.load()]);
    await source.load();
    expect(importer).toHaveBeenCalledOnce();
    expect(source.loaded()).toBe(Big);
  });

  it('throws a load that fails to the error boundary, and can be loaded again', async () => {
    vi.spyOn(console, 'error').mockImplementation(() => undefined);
    const first = deferred();
    const importer = vi.fn<() => Promise<typeof Big>>(() => first.promise);
    const source = chunk(importer);
    render(
      <Boundary>
        <Shell source={source} wanted />
      </Boundary>,
    );
    await act(async () => {
      first.reject(new Error('the chunk couldn’t be fetched'));
      await first.promise.catch(() => undefined);
    });
    expect(screen.getByRole('alert')).toHaveTextContent('the chunk couldn’t be fetched');
    // The failed load isn't kept: the next ask fetches again.
    importer.mockImplementation(() => Promise.resolve(Big));
    await expect(source.load()).resolves.toBe(Big);
    expect(importer).toHaveBeenCalledTimes(2);
  });
});
