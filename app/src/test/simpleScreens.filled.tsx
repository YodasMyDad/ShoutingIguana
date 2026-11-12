// The routes' table with every row set, as round 4 will set them: each Simple screen a stub that names itself. Served in
// place of `app/simpleScreens.ts` by `scripts/filled-table.mjs`, for the unit suite's `filled` project and
// `npm run e2e:filled`. A frame that works with these works whichever rows round 4 sets.
import { useParams } from '@tanstack/react-router';
import type { SimpleScreen, SimpleScreens } from '../app/simpleScreens';

export { simpleScreen, type SimpleScreen, type SimpleScreenKey, type SimpleScreens } from '../app/simpleScreens';

function stub(name: string): SimpleScreen {
  function StubScreen() {
    const { check } = useParams({ strict: false });
    return (
      <div className="flex flex-col gap-2 px-(--page-gutter) py-6" data-stub-screen={name}>
        <h1 className="text-24 font-semibold">{name}</h1>
        {check ? <p>{check}</p> : null}
      </div>
    );
  }
  return Object.assign(StubScreen, { preload: () => Promise.resolve() }) as SimpleScreen;
}

export const SIMPLE_SCREENS: SimpleScreens = {
  home: stub('Simple Home (stub)'),
  overview: stub('Summary (stub)'),
  issues: stub('What to fix (stub)'),
  report: stub('Report (stub)'),
};
