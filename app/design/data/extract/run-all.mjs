// Regenerates every fixture: copy the crawls, analyse the copies through the sidecar and save its API, extract the URL
// grids, inspector, live replay and brand colours from the copies, then cross-check everything and print the summary.
// Arguments are passed to every step (see lib/options.mjs or --help).
import { spawnSync } from 'node:child_process';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const steps = ['copy-crawls.mjs', 'sidecar-api.mjs', 'urls.mjs', 'inspector.mjs', 'live.mjs', 'brand.mjs', 'summary.mjs'];
const args = process.argv.slice(2);

for (const step of steps) {
    console.log(`\n== ${step}`);
    const started = Date.now();
    const result = spawnSync(process.execPath, ['--disable-warning=ExperimentalWarning', join(here, step), ...args], { stdio: 'inherit' });
    if (result.status !== 0) {
        console.error(`${step} failed (exit ${result.status}).`);
        process.exit(result.status ?? 1);
    }

    console.log(`== ${step} done in ${((Date.now() - started) / 1000).toFixed(1)} s`);
}
