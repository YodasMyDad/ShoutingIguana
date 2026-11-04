import { act, fireEvent, render, screen } from '@testing-library/react';
import { RefreshCw } from 'lucide-react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { commands } from '../keyboard/registry';
import { BUTTON_SPINNER_DELAY_MS, Button } from './Button';
import { IconButton } from './IconButton';

afterEach(() => vi.useRealTimers());

const spinnerOf = (button: HTMLElement) => button.querySelector('.si-spinner');

describe('Button', () => {
  it('shows a spinner only once work has taken 300 ms, and ignores presses meanwhile', () => {
    vi.useFakeTimers();
    const onClick = vi.fn();
    const { rerender } = render(<Button onClick={onClick}>Analyse again</Button>);
    const button = screen.getByRole('button', { name: 'Analyse again' });
    fireEvent.click(button);
    expect(onClick).toHaveBeenCalledOnce();

    rerender(
      <Button onClick={onClick} loading>
        Analyse again
      </Button>,
    );
    expect(button).toHaveAttribute('aria-busy', 'true');
    expect(button).toHaveAttribute('aria-disabled', 'true');
    expect(spinnerOf(button)).toBeNull();
    fireEvent.click(button);
    expect(onClick).toHaveBeenCalledOnce();

    act(() => {
      vi.advanceTimersByTime(BUTTON_SPINNER_DELAY_MS - 1);
    });
    expect(spinnerOf(button)).toBeNull();
    act(() => {
      vi.advanceTimersByTime(1);
    });
    // Over the label, which keeps its room: the button doesn't change width, and keeps its name.
    expect(spinnerOf(button)).not.toBeNull();
    expect(button).toHaveAttribute('data-spinner', 'cover');
    expect(screen.getByRole('button', { name: 'Analyse again' })).toBe(button);
  });

  it('puts the spinner in place of its icon, when it has one', () => {
    vi.useFakeTimers();
    render(
      <Button icon={RefreshCw} loading>
        Crawl again
      </Button>,
    );
    act(() => {
      vi.advanceTimersByTime(BUTTON_SPINNER_DELAY_MS);
    });
    const button = screen.getByRole('button', { name: 'Crawl again' });
    expect(spinnerOf(button)).not.toBeNull();
    expect(button).not.toHaveAttribute('data-spinner');
    expect(button.querySelectorAll('svg')).toHaveLength(1);
  });

  it('never shows a spinner for work that ends first', () => {
    vi.useFakeTimers();
    const { rerender } = render(<Button loading>Pause</Button>);
    act(() => {
      vi.advanceTimersByTime(BUTTON_SPINNER_DELAY_MS / 2);
    });
    rerender(<Button loading={false}>Pause</Button>);
    act(() => {
      vi.advanceTimersByTime(BUTTON_SPINNER_DELAY_MS);
    });
    expect(spinnerOf(screen.getByRole('button', { name: 'Pause' }))).toBeNull();
  });

  it('gives a command’s keys to screen readers and to its tooltip, which shows on focus', async () => {
    const unregister = commands.register([
      { id: 'test.pause', title: 'Pause the crawl', group: 'Crawl', keys: ['f7'] },
    ]);
    try {
      render(<Button command="test.pause">Pause</Button>);
      const button = screen.getByRole('button', { name: 'Pause' });
      expect(button).toHaveAttribute('aria-keyshortcuts', 'F7');
      act(() => button.focus());
      await screen.findByRole('tooltip');
      // The tooltip shows "Pause" and F7; it describes the button by what its name lacks, the keys.
      expect(button).toHaveAccessibleDescription('F7');
    } finally {
      unregister();
    }
  });

  it('shows its keys inside itself with showShortcut, named by its words alone', () => {
    const unregister = commands.register([
      { id: 'test.pause', title: 'Pause the crawl', group: 'Crawl', keys: ['f7'] },
    ]);
    try {
      render(
        <Button command="test.pause" showShortcut>
          Pause
        </Button>,
      );
      const button = screen.getByRole('button', { name: 'Pause' });
      expect(button).toHaveTextContent('PauseF7');
      expect(button).toHaveAttribute('aria-keyshortcuts', 'F7');
    } finally {
      unregister();
    }
  });

  it('reads a toggle as pressed', () => {
    render(<Button pressed>Show passed</Button>);
    expect(screen.getByRole('button', { name: 'Show passed' })).toHaveAttribute('aria-pressed', 'true');
  });

  it('keeps disabled buttons inert', () => {
    const onClick = vi.fn();
    render(
      <Button disabled onClick={onClick}>
        Remove
      </Button>,
    );
    fireEvent.click(screen.getByRole('button', { name: 'Remove' }));
    expect(onClick).not.toHaveBeenCalled();
  });
});

describe('IconButton', () => {
  it('is named by its label', () => {
    render(<IconButton label="Close the inspector" icon={RefreshCw} />);
    const button = screen.getByRole('button', { name: 'Close the inspector' });
    expect(button.querySelector('svg')).toHaveAttribute('aria-hidden', 'true');
  });

  it('isn’t described by its tooltip when the tooltip is its label, so its name isn’t read twice', async () => {
    render(<IconButton label="Open in the browser" icon={RefreshCw} />);
    const button = screen.getByRole('button', { name: 'Open in the browser' });
    act(() => button.focus());
    await screen.findByRole('tooltip');
    expect(button).not.toHaveAttribute('aria-describedby');
    expect(button).toHaveAccessibleDescription('');
  });

  it('is described by a tooltip that says more than its label', async () => {
    render(<IconButton label="Show full URLs" tooltip="Show the URLs in full" icon={RefreshCw} />);
    const button = screen.getByRole('button', { name: 'Show full URLs' });
    act(() => button.focus());
    await screen.findByRole('tooltip');
    expect(button).toHaveAccessibleDescription('Show the URLs in full');
  });
});
