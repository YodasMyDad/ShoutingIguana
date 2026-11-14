// A Vite plugin: every build of the app writes licences.json beside it, the third-party licences of what it ships
// (scripts/licences.mjs). Strict in a Tauri build (TAURI_ENV_PLATFORM is set for its beforeBuildCommand) or with
// SI_LICENCES_STRICT=1: a release never ships a partial list. Storybook's build has no About to show it, so it skips.
import { collect } from './licences.mjs';

/** @returns {import('vite').Plugin} */
export function licences() {
  return {
    name: 'si-licences',
    apply: 'build',
    generateBundle() {
      if (process.env.STORYBOOK === 'true') return;
      const strict = Boolean(process.env.TAURI_ENV_PLATFORM) || process.env.SI_LICENCES_STRICT === '1';
      const list = collect({ strict, log: { warn: (message) => this.warn(message) } });
      this.emitFile({ type: 'asset', fileName: 'licences.json', source: JSON.stringify(list) });
    },
  };
}
