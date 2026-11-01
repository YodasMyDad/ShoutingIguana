// Display formatting, following the UI brief's "Showing data" rules: thousands separators, units always, durations
// as "1 min 12 s", UK dates. Times in the fixtures are UTC; in late October and November the UK is on GMT, so the
// clock times shown are the same.

const intFormat = new Intl.NumberFormat('en-GB');

export const int = (n) => intFormat.format(n);

export function decimal(n, digits = 1) {
  return new Intl.NumberFormat('en-GB', { minimumFractionDigits: digits, maximumFractionDigits: digits }).format(n);
}

export function percent(part, whole, digits = 1) {
  if (!whole) return '';
  const p = (part / whole) * 100;
  if (p > 0 && p < 0.1) return '<0.1%';
  return `${decimal(p, digits)}%`;
}

// Binary units, labelled KB and MB, so sizes match what Windows Explorer shows.
export function bytes(n) {
  if (n == null) return '';
  if (n < 1024) return `${int(n)} B`;
  if (n < 1024 * 1024) return `${decimal(n / 1024, 1)} KB`;
  if (n < 1024 * 1024 * 1024) return `${decimal(n / 1024 / 1024, 1)} MB`;
  return `${decimal(n / 1024 / 1024 / 1024, 2)} GB`;
}

export const ms = (n) => (n == null ? '' : `${int(Math.round(n))} ms`);

export function duration(totalMs) {
  const s = Math.round(totalMs / 1000);
  const h = Math.floor(s / 3600);
  const m = Math.floor((s % 3600) / 60);
  const sec = s % 60;
  if (h) return `${h} h ${m} min`;
  if (m) return `${m} min ${sec} s`;
  return `${sec} s`;
}

const months = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
const days = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'];
const pad = (n) => String(n).padStart(2, '0');

export function clock(iso) {
  const d = new Date(iso);
  return `${pad(d.getUTCHours())}:${pad(d.getUTCMinutes())}`;
}

// "31 Oct, 14:13": how a crawl is named in the top bar and sidebar.
export function crawlName(iso) {
  const d = new Date(iso);
  return `${d.getUTCDate()} ${months[d.getUTCMonth()]}, ${clock(iso)}`;
}

// "Sat 31 Oct 2026, 14:13:58": the exact time, for hover.
export function exact(iso) {
  const d = new Date(iso);
  return `${days[d.getUTCDay()]} ${d.getUTCDate()} ${months[d.getUTCMonth()]} ${d.getUTCFullYear()}, ${clock(iso)}:${pad(d.getUTCSeconds())}`;
}

// Relative to the mockup's "now".
export function relative(iso, nowIso) {
  const then = new Date(iso);
  const now = new Date(nowIso);
  const mins = Math.round((now - then) / 60000);
  if (mins < 1) return 'Just now';
  if (mins < 60) return `${mins} min ago`;
  const startOfToday = Date.UTC(now.getUTCFullYear(), now.getUTCMonth(), now.getUTCDate());
  if (then.getTime() >= startOfToday) return `${Math.round(mins / 60)} h ago`;
  const daysAgo = Math.ceil((startOfToday - then.getTime()) / 86400000);
  if (daysAgo === 1) return 'Yesterday';
  if (daysAgo < 7) return `${daysAgo} days ago`;
  return `${then.getUTCDate()} ${months[then.getUTCMonth()]}`;
}

export function esc(s) {
  return String(s ?? '')
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;');
}

// Splits a URL for display: the scheme is dropped for https (http stays, since it matters), the host is shown muted
// and the rest strong. A URL longer than its column is cut in one place only, so the host's start and the URL's end
// stay visible. The host shortens to one fixed form (its first 10 characters and its last labels), so a column of
// shortened URLs lines up; only a path too long even for that loses its start. Monospace makes the budget exact.
export function shortHost(host) {
  const labels = host.split('.');
  const tail = labels.length > 2 && labels[labels.length - 2].length <= 3 ? labels.slice(-2).join('.') : labels[labels.length - 1];
  const short = host.slice(0, 10) + '…' + tail;
  return short.length < host.length ? short : host;
}

export function urlParts(url, budget = Infinity) {
  const u = new URL(url);
  const scheme = u.protocol === 'https:' ? '' : `${u.protocol}//`;
  let host = scheme + u.host;
  let rest = u.pathname + u.search + u.hash;
  if (host.length + rest.length > budget) {
    host = scheme + shortHost(u.host);
    if (host.length + rest.length > budget) {
      host = host.slice(0, 10) + '…';
      const room = budget - host.length;
      if (rest.length > room) rest = rest.slice(rest.length - room);
    }
  }
  return { host, rest, full: url };
}

// A path for compact display: "/harpswell-metal-patio-set/".
export const pathOf = (url) => {
  const u = new URL(url);
  return u.pathname + u.search;
};
