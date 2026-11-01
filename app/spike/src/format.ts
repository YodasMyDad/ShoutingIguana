const integer = new Intl.NumberFormat('en-GB', { maximumFractionDigits: 0 });
const oneDecimal = new Intl.NumberFormat('en-GB', { minimumFractionDigits: 1, maximumFractionDigits: 1 });

export const formatInteger = (value: number) => integer.format(value);

export function formatBytes(bytes: number) {
  if (bytes < 1_024) return `${integer.format(bytes)} B`;
  if (bytes < 1_048_576) return `${oneDecimal.format(bytes / 1_024)} KB`;
  return `${oneDecimal.format(bytes / 1_048_576)} MB`;
}

export const formatMs = (ms: number) => `${integer.format(ms)} ms`;

export function statusClass(code: number) {
  return code >= 500 ? 's5xx' : code >= 400 ? 's4xx' : code >= 300 ? 's3xx' : 's2xx';
}

/** Splits a URL so the host and the end stay visible: the head truncates with an ellipsis, the tail never does. */
export function splitUrl(url: string) {
  const tailLength = Math.min(28, Math.floor(url.length / 2));
  return { head: url.slice(0, url.length - tailLength), tail: url.slice(url.length - tailLength) };
}
