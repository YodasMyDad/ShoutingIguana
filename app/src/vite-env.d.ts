/// <reference types="vite/client" />

interface ImportMetaEnv {
  /** `mock` serves the design fixtures instead of calling a sidecar. */
  readonly VITE_TRANSPORT?: string;
  /** A plain browser's development sidecar, set by `npm run dev` (scripts/dev.mjs). */
  readonly VITE_SIDECAR_PORT?: string;
  readonly VITE_SIDECAR_TOKEN?: string;
}

interface ImportMeta {
  readonly env: ImportMetaEnv;
}
