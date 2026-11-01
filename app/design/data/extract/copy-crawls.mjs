// Step 1: copies the crawl files (with any -wal and -shm siblings) from the source folder into <work>/crawls. The
// source is only ever read; every later step opens the copies. Run with --help for the options.
import { copyFileSync, existsSync, mkdirSync, rmSync, statSync } from 'node:fs';
import { join } from 'node:path';
import { settings as readSettings } from './lib/options.mjs';

const settings = readSettings();
mkdirSync(settings.crawlsDir, { recursive: true });

for (const [which, name] of Object.entries(settings.files)) {
    const source = join(settings.source, name);
    if (!existsSync(source)) {
        throw new Error(`The ${which} crawl isn't at ${source}. Give --source or --${which}.`);
    }

    const target = join(settings.crawlsDir, name);
    // A fresh copy each run: the API step analyses the copy, and leftovers from a previous run must not linger.
    for (const suffix of ['', '-wal', '-shm', '-lock']) {
        rmSync(target + suffix, { force: true });
    }

    for (const suffix of ['', '-wal', '-shm']) {
        if (existsSync(source + suffix)) {
            copyFileSync(source + suffix, target + suffix);
            const size = statSync(target + suffix).size;
            if (size !== statSync(source + suffix).size) {
                throw new Error(`The copy of ${source + suffix} is incomplete.`);
            }

            console.log(`copied ${name}${suffix} (${size.toLocaleString('en-GB')} bytes)`);
        }
    }
}

console.log(`copies in ${settings.crawlsDir}`);
