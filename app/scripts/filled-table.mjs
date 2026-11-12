// The routes' table filled (docs/v2-ui-architecture.md, The routes' table): a Vite plugin that serves every import of
// `src/app/simpleScreens.ts` from `src/test/simpleScreens.filled.tsx`, which sets every row to a stub screen. The unit
// suite runs once more with it (`vitest.config.ts`, the `filled` project), and the end-to-end tests can
// (`npm run e2e:filled`), so a Simple screen round 4 sets in its row can't break the frame: whatever the frame does
// with a row set is tested before any row is.
import path from 'node:path';

const TABLE = path.join('src', 'app', 'simpleScreens.ts');
const FILLED = path.resolve(import.meta.dirname, '..', 'src', 'test', 'simpleScreens.filled.tsx');

/** @returns {import('vite').Plugin} */
export function filledTable() {
  return {
    name: 'si-filled-table',
    enforce: 'pre',
    async resolveId(source, importer, options) {
      if (!source.includes('simpleScreens') || importer === undefined) return null;
      // The filled table reads the real one for its types and helper.
      if (path.resolve(importer) === FILLED) return null;
      const resolved = await this.resolve(source, importer, { ...options, skipSelf: true });
      return resolved && path.normalize(resolved.id).endsWith(TABLE) ? FILLED : null;
    },
  };
}

/** Whether a build or a test run asks for the filled table: `SI_FILLED_TABLE=1`. */
export const FILLED_TABLE = process.env.SI_FILLED_TABLE === '1';
