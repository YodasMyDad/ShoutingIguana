// Formatting, as the UI brief asks: thousands separators, units always, "1 min 12 s", UK dates.

export const esc = s => String(s ?? '')
  .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');

export const num = n => (n == null ? '' : Number(n).toLocaleString('en-GB'));

export const pct = (part, whole) => {
  if (!whole) return '0%';
  const v = (part / whole) * 100;
  if (v === 0 || v === 100) return `${v}%`;
  if (v < 0.1) return '<0.1%';
  if (v < 10 || v > 99) return `${v.toFixed(1).replace(/.0$/, '')}%`;
  return `${Math.round(v)}%`;
};

// A figure with its unit set smaller and muted.
export const fig = (value, unit) => unit
  ? `<span class="fig">${value}<span class="fig__unit">${unit}</span></span>`
  : `<span class="fig">${value}</span>`;

// Bytes, binary (as Windows shows them), with the unit.
export function bytes(n, { parts = false } = {}) {
  if (n == null) return parts ? ['', ''] : '';
  const units = ['bytes', 'KB', 'MB', 'GB'];
  let v = n, u = 0;
  while (v >= 1024 && u < units.length - 1) { v /= 1024; u++; }
  const value = u === 0 ? num(v) : v >= 100 ? Math.round(v).toLocaleString('en-GB') : v.toFixed(1).replace(/\.0$/, '');
  return parts ? [value, units[u]] : `${value} ${units[u]}`;
}

// "22 min 26 s", "1 h 1 min". Units are joined with no-break spaces so a duration never splits.
// ceil: round up to the minute, for upper bounds such as "at most 1 h 1 min more".
export function duration(ms, { parts = false, ceil = false } = {}) {
  let s = Math.round(ms / 1000);
  if (ceil) s = Math.ceil(s / 60) * 60;
  const h = Math.floor(s / 3600), m = Math.floor((s % 3600) / 60), sec = s % 60;
  let out = h ? [[h, 'h'], [m, 'min']] : m ? [[m, 'min'], [sec, 's']] : [[sec, 's']];
  out = out.filter(([v], i) => i === 0 || v !== 0);
  if (parts) return out;
  return out.map(([v, u]) => `${v} ${u}`).join(' ');
}
export const durationFig = ms => duration(ms, { parts: true })
  .map(([v, u]) => `${v}<span class="fig__unit">${u}</span>`).join('<span class="fig__gap"></span>');

const MONTHS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
// The crawls ran in late October and November 2026: UK time is GMT, so UTC is local.
export function dateShort(iso) {
  const d = new Date(iso);
  return `${d.getUTCDate()} ${MONTHS[d.getUTCMonth()]}`;
}
export function time(iso, { seconds = false } = {}) {
  const d = new Date(iso);
  const hh = String(d.getUTCHours()).padStart(2, '0'), mm = String(d.getUTCMinutes()).padStart(2, '0');
  return seconds ? `${hh}:${mm}:${String(d.getUTCSeconds()).padStart(2, '0')}` : `${hh}:${mm}`;
}
export const dateTime = iso => `${dateShort(iso)}, ${time(iso)}`;
export function dateLong(iso) {
  const d = new Date(iso);
  return `${d.getUTCDate()} ${['January', 'February', 'March', 'April', 'May', 'June', 'July', 'August', 'September', 'October', 'November', 'December'][d.getUTCMonth()]} ${d.getUTCFullYear()}, ${time(iso)}`;
}
// Relative to a fixed "now" for the mockup.
export function relative(iso, nowIso) {
  const diff = (new Date(nowIso) - new Date(iso)) / 1000;
  if (diff < 60) return 'Just now';
  if (diff < 3600) { const m = Math.round(diff / 60); return `${m} min ago`; }
  const now = new Date(nowIso), d = new Date(iso);
  const days = Math.floor((Date.UTC(now.getUTCFullYear(), now.getUTCMonth(), now.getUTCDate()) - Date.UTC(d.getUTCFullYear(), d.getUTCMonth(), d.getUTCDate())) / 86400000);
  if (days === 0) { const h = Math.round(diff / 3600); return `${h} h ago`; }
  if (days === 1) return `Yesterday, ${time(iso)}`;
  if (days < 7) return `${days} days ago`;
  return dateShort(iso);
}

// ---- URLs: host muted, path strong, truncated in the middle of the path so the host and the end stay visible.
export function splitUrl(url) {
  const m = url.match(/^(https?:\/\/)([^/]+)(.*)$/);
  if (!m) return { scheme: '', host: url, path: '' };
  return { scheme: m[1], host: m[2], path: m[3] || '/' };
}
export function middle(text, max) {
  if (text.length <= max) return text;
  const keepEnd = Math.ceil((max - 1) * 0.55), keepStart = max - 1 - keepEnd;
  return text.slice(0, keepStart) + '…' + text.slice(text.length - keepEnd);
}
// Renders a URL as spans. budget: characters for the whole URL (scheme hidden for https).
export function urlHtml(url, { budget = 999, showScheme = false, hostMode = 'full' } = {}) {
  const { scheme, host, path } = splitUrl(url);
  const sch = showScheme || scheme === 'http://' ? scheme : '';
  const hostShown = hostMode === 'none' ? '' : host;
  const room = Math.max(8, budget - sch.length - hostShown.length);
  const p = middle(path, room);
  return `<span class="url" title="${esc(url)}">${sch ? `<span class="url__scheme">${esc(sch)}</span>` : ''}${hostShown ? `<span class="url__host">${esc(hostShown)}</span>` : ''}<span class="url__path">${esc(p)}</span></span>`;
}

export const plural = (n, one, many) => `${num(n)} ${n === 1 ? one : many}`;
