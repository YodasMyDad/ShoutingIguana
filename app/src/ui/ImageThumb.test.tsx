import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { ImageThumb } from './ImageThumb';

describe('an image’s thumbnail', () => {
  it('asks for the image lazily, without a referrer, named by its file, with the icon until it loads', () => {
    const { container } = render(<ImageThumb url="https://example.com/img/hero.jpg?v=2" />);
    const image = screen.getByRole('img', { name: 'hero.jpg' });
    expect(image).toHaveAttribute('src', 'https://example.com/img/hero.jpg?v=2');
    expect(image).toHaveAttribute('loading', 'lazy');
    expect(image).toHaveAttribute('referrerpolicy', 'no-referrer');
    expect(container.firstElementChild).toHaveAttribute('data-thumb', 'loading');
    fireEvent.load(image);
    expect(container.firstElementChild).toHaveAttribute('data-thumb', 'loaded');
  });

  it('keeps its box with the icon when the image can’t be read, and never asks for one known to be broken', () => {
    const { container, rerender } = render(<ImageThumb url="https://example.com/a.png" />);
    fireEvent.error(screen.getByRole('img', { name: 'a.png' }));
    expect(screen.queryByRole('img')).toBeNull();
    expect(container.firstElementChild).toHaveAttribute('data-thumb', 'failed');
    // Another image starts afresh.
    rerender(<ImageThumb url="https://example.com/b.png" />);
    expect(screen.getByRole('img', { name: 'b.png' })).toBeInTheDocument();
    rerender(<ImageThumb url="https://example.com/gone.png" broken />);
    expect(screen.queryByRole('img')).toBeNull();
  });

  it('draws a neutral image icon for one that couldn’t be read, and the broken one only for an image known to be broken', () => {
    const { container, rerender } = render(<ImageThumb url="https://example.com/offline.png" />);
    // Loading: the neutral icon stands in.
    expect(container.querySelector('.lucide-image')).not.toBeNull();
    fireEvent.error(screen.getByRole('img', { name: 'offline.png' }));
    // Not read (offline, a policy that refuses it, not loaded yet): nothing says it's broken.
    expect(container.querySelector('.lucide-image-off')).toBeNull();
    expect(container.querySelector('.lucide-image')).not.toBeNull();
    rerender(<ImageThumb url="https://example.com/gone.png" broken />);
    expect(container.firstElementChild).toHaveAttribute('data-thumb', 'broken');
    expect(container.querySelector('.lucide-image-off')).not.toBeNull();
  });
});
