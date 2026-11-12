import { describe, expect, it } from 'vitest';
import { appDiagnostics, withoutAccountName } from './diagnostics';

describe('diagnostics as they can be sent (review finding 17)', () => {
  it('stand %USERPROFILE% for the home folder, and the account name nowhere', async () => {
    const copied = await appDiagnostics(
      {
        version: '1',
        engineVersion: '2',
        dataDir: 'C:\\Users\\Jo.Bloggs\\Documents\\Shouting Iguana',
        stateDir: 'c:\\users\\jo.bloggs\\AppData\\Local\\ShoutingIguana',
        pid: 4,
      },
      { mode: 'simple', theme: 'system', density: 'standard', zoom: 1 },
    );
    expect(copied).toContain('Data folder: %USERPROFILE%\\Documents\\Shouting Iguana');
    expect(copied).toContain('State folder: %USERPROFILE%\\AppData\\Local\\ShoutingIguana');
    expect(copied.toLowerCase()).not.toContain('jo.bloggs');
  });

  it('takes the name out wherever else it is, and leaves other text alone', () => {
    expect(withoutAccountName('Log: C:\\Users\\sam\\logs\nWho: sam\nSampler: on')).toBe(
      'Log: %USERPROFILE%\\logs\nWho: <user>\nSampler: on',
    );
    expect(withoutAccountName('Data: /Users/ana/Library/x and /home/ana/y')).toBe('Data: ~/Library/x and ~/y');
    expect(withoutAccountName('Data folder: unknown')).toBe('Data folder: unknown');
  });
});
