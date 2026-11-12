// Starts si-sidecar on the copies and talks to it, for the steps that record what the API answers. As the contract's
// process section says (docs/v2-sidecar-api.md): the token in SI_SIDECAR_TOKEN, the ready line on stdout, a lifeline
// on stdin, and shutdown through POST /api/shutdown.
import { spawn } from 'node:child_process';
import { randomBytes } from 'node:crypto';
import { mkdirSync } from 'node:fs';
import { createInterface } from 'node:readline';

const ANALYSIS_TIMEOUT_MS = 15 * 60_000;
const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));

/**
 * @param {{ sidecar: string | null, crawlsDir: string }} settings
 * @param {string} stateDir A state folder of this run's own: two sidecars never share one.
 */
export async function startSidecar(settings, stateDir) {
    if (!settings.sidecar) {
        throw new Error('No si-sidecar build found. Build engine/src/ShoutingIguana.Sidecar or pass --sidecar <path>.');
    }

    mkdirSync(stateDir, { recursive: true });
    const token = randomBytes(32).toString('hex');
    const [command, prefix] = settings.sidecar.toLowerCase().endsWith('.dll') ? ['dotnet', [settings.sidecar]] : [settings.sidecar, []];
    const child = spawn(command, [...prefix, '--data-dir', settings.crawlsDir, '--state-dir', stateDir, '--stdin-lifeline'], {
        env: { ...process.env, SI_SIDECAR_TOKEN: token, SI_PARENT_PID: String(process.pid) },
        stdio: ['pipe', 'pipe', 'ignore'],
        windowsHide: true
    });
    const exited = new Promise(resolve => child.on('exit', code => resolve(code)));
    const readyLine = await new Promise((resolve, reject) => {
        createInterface({ input: child.stdout }).once('line', line => {
            try {
                resolve(JSON.parse(line));
            } catch {
                reject(new Error(`The first stdout line isn't the ready line: ${line}`));
            }
        });
        exited.then(code => reject(new Error(`si-sidecar exited with ${code} before it was ready`)));
    });
    const base = `http://127.0.0.1:${readyLine.port}`;

    /** A call that must succeed: its JSON, or an error naming what the sidecar said. */
    async function call(method, path, body) {
        const headers = { authorization: `Bearer ${token}` };
        if (body !== undefined) headers['content-type'] = 'application/json';
        const response = await fetch(base + path, { method, headers, body: body === undefined ? undefined : JSON.stringify(body) });
        const text = await response.text();
        if (!response.ok) {
            throw new Error(`${method} ${path} gave ${response.status}: ${text}`);
        }

        return text ? JSON.parse(text) : null;
    }

    return {
        version: readyLine.version,
        get: path => call('GET', path),
        post: (path, body = {}) => call('POST', path, body),

        /** Analyses a crawl with this engine and waits for it to finish. */
        async analyse(id) {
            await call('POST', `/api/crawls/${id}/analyze`);
            const deadline = Date.now() + ANALYSIS_TIMEOUT_MS;
            for (;;) {
                await sleep(500);
                const item = await call('GET', `/api/crawls/${id}`);
                if (item.status !== 'analyzing') return item;
                if (Date.now() > deadline) throw new Error(`The analysis of ${id} didn't finish in 15 minutes.`);
            }
        },

        async stop() {
            await call('POST', '/api/shutdown').catch(() => undefined);
            let timer;
            const code = await Promise.race([exited, new Promise(resolve => (timer = setTimeout(() => resolve(null), 30_000)))]);
            clearTimeout(timer);
            if (code === null) child.kill();
        }
    };
}
