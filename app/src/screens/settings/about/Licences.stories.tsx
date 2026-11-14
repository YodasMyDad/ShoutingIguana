import type { Meta, StoryObj } from '@storybook/react-vite';
import { StateRow, StoryFrame, StorySection } from '../../../test/StoryFrame';
import { Licences } from './Licences';
import type { LicenceList } from './licenceList';

const meta = { title: 'Settings/Licences' } satisfies Meta;
export default meta;
type Story = StoryObj<typeof meta>;

const MIT = `MIT License

Copyright (c) Meta Platforms, Inc. and affiliates.

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated
documentation files (the "Software"), to deal in the Software without restriction.`;

const SAMPLE: LicenceList = {
  generatedAt: '2026-11-14T14:00:00Z',
  complete: true,
  texts: [MIT, 'Apache License\nVersion 2.0, January 2004', 'SIL Open Font License, Version 1.1'],
  packages: [
    {
      name: 'react',
      version: '19.3.1',
      source: 'npm',
      licence: 'MIT',
      url: 'https://www.npmjs.com/package/react',
      text: 0,
    },
    {
      name: '@radix-ui/react-dropdown-menu',
      version: '2.2.1',
      source: 'npm',
      licence: 'MIT',
      url: 'https://www.npmjs.com/package/@radix-ui/react-dropdown-menu',
      text: 0,
    },
    {
      name: 'tauri',
      version: '2.12.4',
      source: 'cargo',
      licence: 'Apache-2.0 OR MIT',
      url: 'https://github.com/tauri-apps/tauri',
      text: 1,
    },
    {
      name: 'windows-sys-with-a-very-long-crate-name-for-the-narrow-panel',
      version: '0.61.2',
      source: 'cargo',
      licence: 'MIT OR Apache-2.0',
      url: null,
      text: null,
    },
    {
      name: 'Serilog',
      version: '4.3.0',
      source: 'nuget',
      licence: 'Apache-2.0',
      url: 'https://www.nuget.org/packages/Serilog/4.3.0',
      text: null,
    },
    { name: 'Inter', version: '', source: 'font', licence: 'OFL-1.1', url: null, text: 2 },
  ],
};

/** Every state: read, the build made none (run from source), the list unreadable, and the list itself. */
export const EveryState: Story = {
  render: () => (
    <StoryFrame sheet>
      <StorySection title="About › Third-party licences">
        <StateRow label="Reading">
          <Licences state={{ kind: 'loading' }} />
        </StateRow>
        <StateRow label="Run from source: no list">
          <Licences state={{ kind: 'missing' }} />
        </StateRow>
        <StateRow label="The list couldn't be read">
          <Licences state={{ kind: 'failed' }} />
        </StateRow>
        <StateRow label="Closed">
          <Licences state={{ kind: 'ready', list: SAMPLE }} />
        </StateRow>
        <StateRow label="A build that couldn't read every source">
          <Licences state={{ kind: 'ready', list: { ...SAMPLE, complete: false } }} />
        </StateRow>
        <StateRow label="Open">
          <div className="max-w-(--form-max)">
            <Licences state={{ kind: 'ready', list: SAMPLE }} defaultOpen />
          </div>
        </StateRow>
      </StorySection>
    </StoryFrame>
  ),
};

/** A narrow panel, as at 200% zoom: long names wrap, nothing overprints. */
export const Narrow: Story = {
  render: () => (
    <StoryFrame>
      <div className="w-80">
        <Licences state={{ kind: 'ready', list: SAMPLE }} defaultOpen />
      </div>
    </StoryFrame>
  ),
};
