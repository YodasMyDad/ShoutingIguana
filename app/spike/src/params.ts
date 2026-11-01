// Spike variant switches: from the page URL in the browser bench, or injected by the Tauri shell (SPIKE_QUERY) before
// any script runs, because a query string on the app URL does not survive Tauri's asset protocol reliably.
const injected = (window as unknown as { __SPIKE_QUERY__?: string }).__SPIKE_QUERY__ ?? '';

export const params = new URLSearchParams(location.search);
for (const [key, value] of new URLSearchParams(injected)) {
  if (!params.has(key)) params.set(key, value);
}
