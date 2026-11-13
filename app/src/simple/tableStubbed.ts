// For the tests of Simple's screens through the whole app: the unit suite's `filled` project serves the routes' table
// with a stub in every row (scripts/filled-table.mjs), where these screens aren't drawn, so their tests run in the
// `app` project only. The frame's own tests cover the filled table.
import { SIMPLE_SCREENS } from '../app/simpleScreens';

/** The routes' table is the filled one: its rows are the stubs that name themselves. */
export const TABLE_STUBBED = (SIMPLE_SCREENS.home as { name?: string } | null)?.name === 'StubScreen';
