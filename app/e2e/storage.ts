// What a spec's browser starts with: the app's preferences as a plain browser keeps them (localStorage).
import { PREVIEW_PORT } from '../scripts/ports.mjs';

export const BASE_URL = `http://localhost:${PREVIEW_PORT}`;

/**
 * Every spec starts in Advanced, which the screens built before Simple were written for. A spec of Simple, or of a
 * first start, starts from nothing stored (`test.use({ storageState: { cookies: [], origins: [] } })`).
 */
export const ADVANCED_STORAGE = {
  cookies: [],
  origins: [{ origin: BASE_URL, localStorage: [{ name: 'si.settings', value: JSON.stringify({ mode: 'advanced' }) }] }],
};
