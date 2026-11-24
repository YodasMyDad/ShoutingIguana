// Crawls one address through a built sidecar with the browser fallback on, as the desktop app asks for it, and says
// what the crawl got and what it said. It proves the fallback's own browser starts from the packaged, single-file
// sidecar, which `smoke-sidecar.mjs` doesn't ask of it. A few URLs, one at a time.
//
//   node fallback-sidecar.mjs --sidecar <path to si-sidecar> --url <address> [--urls 4] [--wait 300]
//
// A check that needs a person opens a browser window: complete it and the crawl goes on.
import { spawn } from "node:child_process";
import { randomBytes } from "node:crypto";
import { mkdtempSync, rmSync } from "node:fs";
import os from "node:os";
import path from "node:path";
import { createInterface } from "node:readline";

const argv = process.argv.slice(2);
const option = (name, fallback) =>
  argv.includes(name) ? argv[argv.indexOf(name) + 1] : fallback;
const sidecar = option("--sidecar");
const url = option("--url");
if (!sidecar || !url) {
  console.error(
    "usage: node fallback-sidecar.mjs --sidecar <path> --url <address> [--urls 4] [--wait 300]",
  );
  process.exit(2);
}
const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));
const temp = mkdtempSync(path.join(os.tmpdir(), "si-fallback-sidecar-"));
const token = randomBytes(32).toString("hex");
const child = spawn(
  path.resolve(sidecar),
  [
    "--data-dir",
    path.join(temp, "data"),
    "--state-dir",
    path.join(temp, "state"),
    "--stdin-lifeline",
  ],
  {
    env: {
      ...process.env,
      SI_SIDECAR_TOKEN: token,
      DOTNET_BUNDLE_EXTRACT_BASE_DIR: path.join(temp, "engine"),
    },
    stdio: ["pipe", "pipe", "inherit"],
  },
);
let failed = false;
try {
  const ready = await new Promise((resolve, reject) => {
    createInterface({ input: child.stdout }).on("line", (line) => {
      try {
        const parsed = JSON.parse(line);
        if (parsed.event === "ready") resolve(parsed);
      } catch {
        // Not the ready line. Anything else on standard output is a fault: the browser must not write there.
        reject(new Error(`the sidecar wrote to standard output: ${line}`));
      }
    });
    child.once("exit", (code) =>
      reject(new Error(`the sidecar ended with ${code} before it was ready`)),
    );
  });
  const call = async (method, route, body) => {
    const response = await fetch(`http://127.0.0.1:${ready.port}${route}`, {
      method,
      headers: {
        authorization: `Bearer ${token}`,
        ...(body ? { "content-type": "application/json" } : {}),
      },
      body: body ? JSON.stringify(body) : undefined,
    });
    const text = await response.text();
    return {
      status: response.status,
      json: text ? JSON.parse(text) : undefined,
    };
  };
  const started = await call("POST", "/api/crawls", {
    options: {
      startUrl: url,
      maxUrls: Number(option("--urls", "4")),
      concurrency: 1,
      maxRequestsPerSecond: 0.5,
      checkResources: false,
      checkExternalLinks: false,
      discoverSitemaps: false,
      stayInStartFolder: false,
      browserFallback: true,
      solveChallenges: true,
      challengeWaitSeconds: Number(option("--wait", "300")),
    },
  });
  if (started.status !== 201)
    throw new Error(
      `POST /api/crawls answered ${started.status}: ${JSON.stringify(started.json)}`,
    );
  const id = started.json.id;
  let item;
  for (;;) {
    item = (await call("GET", "/api/crawls")).json?.find(
      (candidate) => candidate.id === id,
    );
    if (item?.status === "completed" && item.analyzedAt) break;
    if (
      item &&
      ["failed", "unreadable", "interrupted", "stopped"].includes(item.status)
    )
      break;
    await sleep(500);
  }
  const alerts = (await call("GET", `/api/crawls/${id}/alerts`)).json ?? [];
  console.log(
    `fallback-sidecar: ${item.status}, ${item.pageCount} pages of ${item.crawledUrlCount ?? "?"} URLs`,
  );
  for (const alert of Array.isArray(alerts)
    ? alerts
    : (alerts.alerts ?? alerts.items ?? []))
    console.log(`  ${alert.level} ${alert.code}: ${alert.message}`);
} catch (error) {
  failed = true;
  console.error(`fallback-sidecar: FAIL: ${error.message}`);
} finally {
  child.stdin.end();
  await Promise.race([
    new Promise((resolve) => child.once("exit", resolve)),
    sleep(15_000),
  ]);
  child.kill();
  try {
    rmSync(temp, { recursive: true, force: true });
  } catch {
    // The browser's last processes can still hold its profile for a moment.
  }
}
process.exit(failed ? 1 : 0);
