import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { Licences } from './Licences';
import type { LicenceList } from './licenceList';

const LIST: LicenceList = {
  generatedAt: '2026-11-14T14:00:00Z',
  complete: true,
  texts: ['MIT License text', 'Apache License text'],
  packages: [
    {
      name: 'react',
      version: '19.3.1',
      source: 'npm',
      licence: 'MIT',
      url: 'https://www.npmjs.com/package/react',
      text: 0,
    },
    { name: 'tauri', version: '2.12.4', source: 'cargo', licence: 'Apache-2.0 OR MIT', url: null, text: 1 },
    { name: 'Serilog', version: '4.3.0', source: 'nuget', licence: 'Apache-2.0', url: null, text: null },
  ],
};

describe('Licences (gap 31)', () => {
  it('says how many packages from where, and lists each with its licence and text on asking', async () => {
    const user = userEvent.setup();
    render(<Licences state={{ kind: 'ready', list: LIST }} />);
    expect(
      screen.getByText('3 packages: 1 in the window (npm), 1 in the app (Rust), 1 in the engine (.NET).'),
    ).toBeInTheDocument();
    expect(screen.queryByRole('list', { name: 'Third-party licences' })).toBeNull();
    await user.click(screen.getByRole('button', { name: 'Show the licences' }));
    const list = screen.getByRole('list', { name: 'Third-party licences' });
    expect(list.querySelectorAll('li')).toHaveLength(3);
    await user.click(screen.getByRole('button', { name: 'tauri' }));
    expect(screen.getByText('Apache License text')).toBeInTheDocument();
    await user.type(screen.getByRole('searchbox', { name: 'Find a package or a licence' }), 'serilog');
    expect(list.querySelectorAll('li')).toHaveLength(1);
    expect(list).toHaveTextContent('Serilog');
  });

  it('says a copy run from source has no list', () => {
    render(<Licences state={{ kind: 'missing' }} />);
    expect(screen.getByText(/made when the app is built/)).toBeInTheDocument();
  });
});
