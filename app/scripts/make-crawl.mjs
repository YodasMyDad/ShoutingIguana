// `npm run make:crawl -- [--out <file>] [--pages <n>] [--engine-root <folder>]`: a small analysed crawl of the
// engine's synthetic site, for development and tests, made with the CLI's testsite.
import path from 'node:path';
import { appRoot, makeSyntheticCrawl } from './lib/engine.mjs';

const argv = process.argv.slice(2);
const option = (name, fallback) => {
  const index = argv.indexOf(name);
  return index >= 0 && argv[index + 1] ? argv[index + 1] : fallback;
};

const out = path.resolve(option('--out', path.join(appRoot, '.engine-build', 'crawls', 'synthetic-small.sicrawl')));
const pages = Number(option('--pages', '60'));

try {
  await makeSyntheticCrawl({ out, pages });
  console.log(`Made ${out}`);
} catch (error) {
  console.error(`make-crawl: ${error instanceof Error ? error.message : String(error)}`);
  process.exit(1);
}
