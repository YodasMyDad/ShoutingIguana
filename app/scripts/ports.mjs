// Fixed ports for development, shared by the Vite config, the dev and end-to-end scripts and the Tauri shell's devUrl.

/** Vite's dev server. `src-tauri/tauri.conf.json` names it in `build.devUrl`. */
export const DEV_SERVER_PORT = 51874;

/** The sidecar `npm run dev` starts for a plain browser. */
export const DEV_SIDECAR_PORT = 51875;

/** `vite preview`, which the end-to-end tests load. */
export const PREVIEW_PORT = 51876;

/** The sidecar the end-to-end tests start. */
export const E2E_SIDECAR_PORT = 51877;
