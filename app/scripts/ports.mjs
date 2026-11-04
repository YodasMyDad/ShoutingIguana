// Ports for development, shared by the Vite config, the dev and end-to-end scripts and the Tauri shell's devUrl. Each
// has a fixed default and an environment variable to move it, so two runs on one machine needn't collide.

/**
 * A port from the environment, or the default. Anything but a whole number from 1 to 65535 is refused.
 * @param {string} variable
 * @param {number} fallback
 * @param {Record<string, string | undefined>} [environment]
 * @returns {number}
 */
export function portFrom(variable, fallback, environment = process.env) {
  const value = environment[variable];
  if (value === undefined || value === '') return fallback;
  const port = Number(value);
  if (!Number.isInteger(port) || port < 1 || port > 65_535) {
    throw new Error(`${variable} must be a port number from 1 to 65535, not '${value}'`);
  }
  return port;
}

/**
 * Vite's dev server (SI_DEV_SERVER_PORT). `src-tauri/tauri.conf.json` names the default in `build.devUrl`, so
 * `npm run tauri dev` on another port also needs `--config` with that devUrl.
 */
export const DEV_SERVER_PORT = portFrom('SI_DEV_SERVER_PORT', 51874);

/** The sidecar `npm run dev` starts for a plain browser (SI_DEV_SIDECAR_PORT). */
export const DEV_SIDECAR_PORT = portFrom('SI_DEV_SIDECAR_PORT', 51875);

/** `vite preview`, which the end-to-end tests load (SI_PREVIEW_PORT). */
export const PREVIEW_PORT = portFrom('SI_PREVIEW_PORT', 51876);

/** The sidecar the end-to-end tests start (SI_E2E_SIDECAR_PORT). */
export const E2E_SIDECAR_PORT = portFrom('SI_E2E_SIDECAR_PORT', 51877);
