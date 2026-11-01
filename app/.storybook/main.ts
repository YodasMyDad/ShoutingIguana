import type { StorybookConfig } from '@storybook/react-vite';

// Storybook shows every component in every state, for the owner to review without running a crawl. It builds with
// the app's own Vite config (Tailwind and the tokens included) and serves the design fixtures through the mock
// transport. Only the app's stories: never the spike's or the design work's.
const config: StorybookConfig = {
  stories: ['../src/**/*.stories.@(ts|tsx)'],
  addons: ['@storybook/addon-a11y'],
  framework: { name: '@storybook/react-vite', options: {} },
  core: { disableTelemetry: true },
};

export default config;
