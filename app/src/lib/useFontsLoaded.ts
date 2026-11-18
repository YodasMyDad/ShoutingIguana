import { useEffect, useState } from 'react';

/**
 * A count that goes up as the page's web fonts arrive: a memo that measures text in them (a column sized to what it
 * holds) depends on it, to measure again, as a text measured in a fallback font draws wider or narrower than that.
 */
export function useFontsLoaded(): number {
  const [loaded, setLoaded] = useState(0);
  useEffect(() => {
    if (typeof document.fonts === 'undefined') return undefined;
    let current = true;
    const arrived = () => {
      if (current) setLoaded((count) => count + 1);
    };
    void document.fonts.ready.then(arrived);
    document.fonts.addEventListener('loadingdone', arrived);
    return () => {
      current = false;
      document.fonts.removeEventListener('loadingdone', arrived);
    };
  }, []);
  return loaded;
}
