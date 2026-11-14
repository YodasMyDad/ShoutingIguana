import type { StorybookConfig } from '@storybook/react-vite';

// Storybook shows every component in every state, for the owner to review without running a crawl. It builds with
// the app's own Vite config (Tailwind and the tokens included) and serves the design fixtures through the mock
// transport. Only the app's stories: never the spike's or the design work's.
const config: StorybookConfig = {
  stories: ['../src/**/*.stories.@(ts|tsx)'],
  // Pseudo-states forces hover, focus and active on chosen elements, so every state can be reviewed without touching.
  addons: ['@storybook/addon-a11y', 'storybook-addon-pseudo-states'],
  framework: { name: '@storybook/react-vite', options: {} },
  core: { disableTelemetry: true },
  // The app's build lists its licences (scripts/licences-plugin.mjs); Storybook has no About to show them.
  viteFinal: (vite) => ({
    ...vite,
    plugins: (vite.plugins ?? [])
      .flat()
      .filter((plugin) => !(plugin && 'name' in plugin && plugin.name === 'si-licences')),
  }),
};

export default config;
