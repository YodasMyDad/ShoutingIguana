import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useState } from 'react';
import { describe, expect, it, vi } from 'vitest';
import { commands, installKeyboard } from '../keyboard/registry';
import { ConfirmDialog } from './Dialog';

describe('Dialog', () => {
  it('owns the keyboard while open: the app’s shortcuts don’t act behind it, its own keys and Esc do', async () => {
    const stop = vi.fn();
    const palette = vi.fn();
    const uninstall = installKeyboard();
    const unregister = commands.register(
      [
        { id: 'test.stop', title: 'Stop the crawl', group: 'Crawl', keys: ['f8'], run: stop },
        { id: 'test.palette', title: 'Open the palette', group: 'App', keys: ['mod+k'], run: palette },
      ],
      'app',
    );
    const onOpenChange = vi.fn();
    const { rerender } = render(
      <ConfirmDialog
        open
        onOpenChange={onOpenChange}
        title="Stop the crawl?"
        description="It can be continued later."
        confirmLabel="Stop crawl"
        onConfirm={() => undefined}
      />,
    );
    const cancel = screen.getByRole('button', { name: 'Cancel' });
    await waitFor(() => expect(cancel).toHaveFocus());
    fireEvent.keyDown(cancel, { key: 'F8' });
    fireEvent.keyDown(cancel, { key: 'k', ctrlKey: true });
    expect(stop).not.toHaveBeenCalled();
    expect(palette).not.toHaveBeenCalled();
    // The dialog's own Esc still closes it.
    fireEvent.keyDown(cancel, { key: 'Escape' });
    expect(onOpenChange).toHaveBeenCalledWith(false);
    // Closed, the app's keys work again.
    rerender(
      <ConfirmDialog
        open={false}
        onOpenChange={onOpenChange}
        title="Stop the crawl?"
        description="It can be continued later."
        confirmLabel="Stop crawl"
        onConfirm={() => undefined}
      />,
    );
    fireEvent.keyDown(document.body, { key: 'F8' });
    expect(stop).toHaveBeenCalledOnce();
    unregister();
    uninstall();
  });
});

describe('ConfirmDialog', () => {
  it('stays open when the action fails, says why in the dialog, and tries again from the same button', async () => {
    const onOpenChange = vi.fn();
    const onConfirm = vi
      .fn<() => Promise<void>>()
      .mockRejectedValueOnce(new Error('EBUSY: the file is open in another program'))
      .mockResolvedValueOnce(undefined);
    render(
      <ConfirmDialog
        open
        onOpenChange={onOpenChange}
        title="Delete the crawl file of example.com?"
        description="The file and its 4,521 URLs are deleted from the disk. This can’t be undone."
        confirmLabel="Delete crawl file"
        destructive
        errorTitle="The crawl file couldn’t be deleted."
        onConfirm={onConfirm}
      />,
    );
    const dialog = screen.getByRole('dialog', { name: 'Delete the crawl file of example.com?' });
    await userEvent.click(screen.getByRole('button', { name: 'Delete crawl file' }));
    const alert = await screen.findByRole('alert');
    expect(dialog).toContainElement(alert);
    expect(alert).toHaveTextContent('The crawl file couldn’t be deleted.');
    // The plain cause first; the engine's words behind Details.
    expect(alert).not.toHaveTextContent('EBUSY');
    await userEvent.click(screen.getByRole('button', { name: 'Details' }));
    expect(alert).toHaveTextContent('EBUSY: the file is open in another program');
    expect(onOpenChange).not.toHaveBeenCalled();

    await userEvent.click(screen.getByRole('button', { name: 'Delete crawl file' }));
    await waitFor(() => expect(onOpenChange).toHaveBeenCalledWith(false));
    expect(screen.queryByRole('alert')).toBeNull();
  });

  it('starts clean when it opens again after a failure', async () => {
    function Delete() {
      const [open, setOpen] = useState(true);
      return (
        <>
          <button type="button" onClick={() => setOpen(true)}>
            Delete…
          </button>
          <ConfirmDialog
            open={open}
            onOpenChange={setOpen}
            title="Delete the file?"
            description="This can’t be undone."
            confirmLabel="Delete crawl file"
            onConfirm={() => Promise.reject(new Error('EBUSY'))}
          />
        </>
      );
    }
    render(<Delete />);
    await userEvent.click(screen.getByRole('button', { name: 'Delete crawl file' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('Something went wrong.');
    await userEvent.click(screen.getByRole('button', { name: 'Cancel' }));
    expect(screen.queryByRole('dialog')).toBeNull();
    await userEvent.click(screen.getByRole('button', { name: 'Delete…' }));
    expect(await screen.findByRole('dialog', { name: 'Delete the file?' })).toBeVisible();
    expect(screen.queryByRole('alert')).toBeNull();
  });
});
