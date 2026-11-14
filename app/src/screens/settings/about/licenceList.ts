// The third-party licences the build listed (scripts/licences.mjs), read when About opens.
import { useEffect, useState } from 'react';

/** One package the app ships, as scripts/licences.mjs lists it. */
export interface LicensedPackage {
  name: string;
  version: string;
  source: 'npm' | 'cargo' | 'nuget' | 'font';
  licence: string;
  url: string | null;
  /** Its licence's text, by its place in `texts`; null when the package carries none. */
  text: number | null;
}

export interface LicenceList {
  generatedAt: string;
  /** False when a source couldn't be read at build time (a development build). */
  complete: boolean;
  packages: LicensedPackage[];
  texts: string[];
}

export type LicencesState =
  { kind: 'loading' } | { kind: 'missing' } | { kind: 'failed' } | { kind: 'ready'; list: LicenceList };

/** The list the build made (vite.config.ts serves it as licences.json), read when About opens. */
export function useLicenceList(): LicencesState {
  const [state, setState] = useState<LicencesState>({ kind: 'loading' });
  useEffect(() => {
    const controller = new AbortController();
    fetch(`${import.meta.env.BASE_URL}licences.json`, { signal: controller.signal })
      .then(async (response) => {
        if (response.status === 404 || !response.ok) {
          setState({ kind: response.status === 404 ? 'missing' : 'failed' });
          return;
        }
        const list = (await response.json()) as LicenceList;
        setState(Array.isArray(list.packages) ? { kind: 'ready', list } : { kind: 'failed' });
      })
      .catch((error: unknown) => {
        if (!controller.signal.aborted) setState({ kind: error instanceof SyntaxError ? 'missing' : 'failed' });
      });
    return () => controller.abort();
  }, []);
  return state;
}
