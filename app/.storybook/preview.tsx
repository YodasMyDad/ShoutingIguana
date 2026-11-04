import type { Preview } from '@storybook/react-vite';
import { createMockTransport } from '../src/api/mock/mockTransport';
import { setTransport } from '../src/api/transport';
import { setDirectConnection } from '../src/shell/connection';
import { TooltipProvider } from '../src/ui/Tooltip';
import '../src/styles/index.css';

// Every story talks to the mock sidecar, which serves the design fixtures (app/design/data/fixtures). Without them,
// data-backed stories show their error state, which says how to regenerate them. A story may answer with a sidecar of
// its own (states the fixtures can't show); every story starts from the mock again.
const mock = createMockTransport();
setTransport(mock);
setDirectConnection({ baseUrl: 'mock://sidecar', token: 'mock', generation: 1, version: 'mock' });

const preview: Preview = {
  globalTypes: {
    theme: {
      description: 'Theme',
      toolbar: { title: 'Theme', icon: 'mirror', items: ['light', 'dark'], dynamicTitle: true },
    },
    density: {
      description: 'Grid density',
      toolbar: { title: 'Density', icon: 'grow', items: ['compact', 'standard', 'comfortable'], dynamicTitle: true },
    },
  },
  initialGlobals: { theme: 'light', density: 'standard' },
  loaders: [() => setTransport(mock)],
  decorators: [
    (Story, context) => {
      const root = document.documentElement;
      root.dataset.theme = String(context.globals.theme ?? 'light');
      root.dataset.density = String(context.globals.density ?? 'standard');
      return (
        <TooltipProvider>
          <Story />
        </TooltipProvider>
      );
    },
  ],
  parameters: {
    layout: 'fullscreen',
    // axe runs on every story; a violation fails the story's accessibility check.
    a11y: { test: 'error' },
  },
};

export default preview;
