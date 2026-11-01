import { describe, expect, it } from 'vitest';
import {
  formatBytes,
  formatCount,
  formatCountOf,
  formatDuration,
  formatMilliseconds,
  formatRelativeTime,
} from './format';

describe('formatting', () => {
  it('uses thousands separators, and a dash for a missing number, never 0', () => {
    expect(formatCount(101107)).toBe('101,107');
    expect(formatCount(null)).toBe('–');
    expect(formatCountOf(1, 'URL')).toBe('1 URL');
    expect(formatCountOf(4521, 'URL')).toBe('4,521 URLs');
  });

  it('gives sizes and times their units', () => {
    expect(formatBytes(512)).toBe('512 bytes');
    expect(formatBytes(18276352)).toBe('17.4 MB');
    expect(formatMilliseconds(312)).toBe('312 ms');
    expect(formatMilliseconds(5476)).toBe('5 s');
  });

  it('reads durations as the brief writes them', () => {
    expect(formatDuration(72_000)).toBe('1 min 12 s');
    expect(formatDuration(2_245_000)).toBe('37 min 25 s');
    expect(formatDuration(7_500_000)).toBe('2 h 5 min');
  });

  it('says relative times in words', () => {
    const now = Date.parse('2026-11-01T12:00:00Z');
    expect(formatRelativeTime('2026-11-01T11:59:50Z', now)).toBe('just now');
    expect(formatRelativeTime('2026-11-01T11:57:00Z', now)).toBe('3 minutes ago');
    expect(formatRelativeTime('2026-10-31T12:00:00Z', now)).toBe('yesterday');
    expect(formatRelativeTime(null, now)).toBe('–');
  });
});
