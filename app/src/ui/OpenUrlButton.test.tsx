import { fireEvent, render, screen } from '@testing-library/react';
import type * as Os from '../shell/os';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { OpenUrlButton } from './OpenUrlButton';
import { Url } from './Url';

const opened = vi.hoisted(() => [] as string[]);
vi.mock('../shell/os', async (original) => ({
  ...(await original<typeof Os>()),
  openInBrowser: (url: string) => {
    opened.push(url);
    return Promise.resolve(true);
  },
}));

afterEach(() => opened.splice(0));

const URL = 'https://www.example.com/blog/best-rattan-patio-sets/';

describe('opening a URL in the browser from where it’s listed', () => {
  it('is a button named by the full URL, which opens that URL through the app’s opener', () => {
    render(<OpenUrlButton url={URL} />);
    const button = screen.getByRole('button', { name: `Open ${URL} in your browser` });
    expect(button.tagName).toBe('BUTTON');
    expect(button.tabIndex).toBe(0);
    fireEvent.click(button);
    expect(opened).toEqual([URL]);
  });

  it('in a grid’s cell, is no Tab stop, keeps focus where it is, and a quick second press is no double-click', () => {
    const onDoubleClick = vi.fn();
    render(
      <div onDoubleClick={onDoubleClick}>
        <Url url={URL} siteOrigin="https://www.example.com" copyable={false} tabbable={false} />
        <OpenUrlButton url={URL} inGrid />
      </div>,
    );
    const buttons = screen.getAllByRole('button', { name: `Open ${URL} in your browser` });
    expect(buttons).toHaveLength(2);
    for (const button of buttons) {
      expect(button.tabIndex).toBe(-1);
      expect(fireEvent.mouseDown(button)).toBe(false);
      fireEvent.click(button);
      fireEvent.doubleClick(button);
    }
    expect(onDoubleClick).not.toHaveBeenCalled();
    // A relative path is only what's shown: the full URL is what opens.
    expect(opened).toEqual([URL, URL]);
  });

  it('offers nothing for an address a browser can’t open', () => {
    render(<OpenUrlButton url="mailto:hello@example.com" />);
    expect(screen.queryByRole('button')).toBeNull();
  });
});
