// A status code's class, for the one meaning status colours have (docs/v2-ui-brief.md, Visual design system).

export type StatusCodeClass = '2xx' | '3xx' | '4xx' | '5xx' | 'none';

/** 200 is `2xx`; no code at all (no response: refused, timed out, blocked) is `none`, as is anything out of range. */
export function statusCodeClass(code: number | null | undefined): StatusCodeClass {
  if (code === null || code === undefined || !Number.isInteger(code)) return 'none';
  if (code >= 200 && code < 300) return '2xx';
  if (code >= 300 && code < 400) return '3xx';
  if (code >= 400 && code < 500) return '4xx';
  if (code >= 500 && code < 600) return '5xx';
  return 'none';
}
