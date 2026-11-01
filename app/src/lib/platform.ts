// Which OS the UI runs on, for shortcuts (Cmd on macOS where Windows uses Ctrl) and wording.

type NavigatorWithUserAgentData = Navigator & { userAgentData?: { platform?: string } };

function detectMac(): boolean {
  if (typeof navigator === 'undefined') return false;
  const nav = navigator as NavigatorWithUserAgentData;
  const platform = nav.userAgentData?.platform ?? nav.platform;
  return /mac|iphone|ipad/i.test(platform);
}

export const isMac: boolean = detectMac();

/** True inside the Tauri shell; false in a plain browser, Storybook and tests. */
export const isTauri: boolean = typeof window !== 'undefined' && '__TAURI_INTERNALS__' in window;
