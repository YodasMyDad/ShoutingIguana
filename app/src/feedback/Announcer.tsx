import { useEffect, useState } from 'react';
import { onAnnounce } from './announce';

/** The live regions. A message is cleared first, so saying the same thing twice is still heard. */
export function Announcer() {
  const [polite, setPolite] = useState('');
  const [assertive, setAssertive] = useState('');

  useEffect(
    () =>
      onAnnounce((message, politeness) => {
        const set = politeness === 'assertive' ? setAssertive : setPolite;
        set('');
        requestAnimationFrame(() => set(message));
      }),
    [],
  );

  return (
    <div className="sr-only">
      <div role="status" aria-live="polite" aria-atomic="true">
        {polite}
      </div>
      <div role="alert" aria-live="assertive" aria-atomic="true">
        {assertive}
      </div>
    </div>
  );
}
