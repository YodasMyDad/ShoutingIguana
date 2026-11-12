import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { Popover } from './Popover';
import { TextField } from './TextField';

describe('where the keyboard starts in a popover', () => {
  it('is the first control of what the popover holds, never the Close button of its head', async () => {
    render(
      <Popover trigger={<button type="button">Columns</button>} title="Columns">
        <TextField label="Find a column" />
        <button type="button">Show all</button>
      </Popover>,
    );
    await userEvent.click(screen.getByRole('button', { name: 'Columns' }));
    const popover = await screen.findByRole('dialog', { name: 'Columns' });
    await waitFor(() => expect(screen.getByRole('textbox', { name: 'Find a column' })).toHaveFocus());
    // Close is still the first in the page's order, a Shift+Tab away.
    expect(popover.querySelector('button')).toBe(screen.getByRole('button', { name: 'Close' }));
    await userEvent.tab({ shift: true });
    expect(screen.getByRole('button', { name: 'Close' })).toHaveFocus();
  });

  it('is the control the content marks as the one to start in, wherever it comes', async () => {
    render(
      <Popover trigger={<button type="button">Status is 404</button>} title="Change the filter">
        <button type="button">Column</button>
        <button type="button">Condition</button>
        <TextField label="Number" defaultValue="404" data-autofocus="" />
        <button type="button">Apply</button>
      </Popover>,
    );
    await userEvent.click(screen.getByRole('button', { name: 'Status is 404' }));
    await screen.findByRole('dialog', { name: 'Change the filter' });
    await waitFor(() => expect(screen.getByRole('textbox', { name: 'Number' })).toHaveFocus());
  });

  it('skips what can’t take the keyboard: a disabled control, one out of the tab order, one that is hidden', async () => {
    render(
      <Popover trigger={<button type="button">Open</button>} title="Options">
        <button type="button" disabled>
          Disabled
        </button>
        <button type="button" tabIndex={-1}>
          Out of the tab order
        </button>
        <div hidden>
          <button type="button">Hidden</button>
        </div>
        <button type="button">First</button>
      </Popover>,
    );
    await userEvent.click(screen.getByRole('button', { name: 'Open' }));
    await screen.findByRole('dialog', { name: 'Options' });
    await waitFor(() => expect(screen.getByRole('button', { name: 'First' })).toHaveFocus());
  });

  it('is Close when the popover holds nothing else to focus, and the popover itself when it has no head', async () => {
    const { unmount } = render(
      <Popover trigger={<button type="button">Why</button>} title="Why this is here">
        <p>Only words.</p>
      </Popover>,
    );
    await userEvent.click(screen.getByRole('button', { name: 'Why' }));
    await screen.findByRole('dialog', { name: 'Why this is here' });
    await waitFor(() => expect(screen.getByRole('button', { name: 'Close' })).toHaveFocus());
    unmount();

    render(
      <Popover trigger={<button type="button">More</button>} label="More to say">
        <p>Only words.</p>
      </Popover>,
    );
    await userEvent.click(screen.getByRole('button', { name: 'More' }));
    const popover = await screen.findByRole('dialog', { name: 'More to say' });
    await waitFor(() => expect(popover).toHaveFocus());
  });
});
