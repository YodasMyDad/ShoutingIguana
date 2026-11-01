// Starts the dummy sidecar the way the Tauri shell does: token in the environment, ready line on stdout.
import { spawn } from 'node:child_process';
import { randomBytes } from 'node:crypto';
import { createInterface } from 'node:readline';
import path from 'node:path';

export const SIDECAR_EXE = path.resolve(import.meta.dirname, '../sidecar/bin/publish/spike-sidecar.exe');

export async function startSidecar({
  allowedOrigins = [],
  parentWatch = true,
  stdinWatch = true,
  latency = '20-50',
  exe = SIDECAR_EXE,
} = {}) {
  const token = randomBytes(32).toString('base64url');
  const started = performance.now();
  const env = { ...process.env, SI_TOKEN: token, SI_ALLOWED_ORIGINS: allowedOrigins.join(','), SI_LATENCY_MS: latency };
  if (parentWatch) env.SI_PARENT_PID = String(process.pid);
  env.SI_WATCH_STDIN = stdinWatch ? '1' : '0';
  const child = spawn(exe, [], { env, stdio: ['pipe', 'pipe', 'pipe'], windowsHide: true });
  const stderrLines = [];
  createInterface({ input: child.stderr }).on('line', (line) => stderrLines.push(line));
  const exited = new Promise((resolve) => child.once('exit', (code) => resolve(code)));
  const port = await new Promise((resolve, reject) => {
    const timer = setTimeout(() => reject(new Error('sidecar ready line timed out')), 15_000);
    createInterface({ input: child.stdout }).on('line', (line) => {
      try {
        const message = JSON.parse(line);
        if (message.event === 'ready') {
          clearTimeout(timer);
          resolve(message.port);
        }
      } catch {
        // not JSON: ignore
      }
    });
    exited.then((code) => {
      clearTimeout(timer);
      reject(new Error(`sidecar exited with ${code} before it was ready`));
    });
  });
  const readyMs = performance.now() - started;
  const baseUrl = `http://127.0.0.1:${port}`;
  return {
    child,
    token,
    port,
    baseUrl,
    readyMs,
    stderrLines,
    exited,
    async stats() {
      const response = await fetch(`${baseUrl}/api/debug/stats`, { headers: { Authorization: `Bearer ${token}` } });
      return response.json();
    },
    async stop() {
      if (child.exitCode !== null) return child.exitCode;
      child.stdin.end('shutdown\n');
      const code = await Promise.race([exited, new Promise((r) => setTimeout(() => r('timeout'), 4_000))]);
      if (code === 'timeout') {
        child.kill();
        return exited;
      }
      return code;
    },
  };
}
