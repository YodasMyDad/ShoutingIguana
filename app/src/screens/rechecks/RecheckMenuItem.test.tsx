import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it } from 'vitest';
import { setTransport } from '../../api/transport';
import { resetPendingRechecks } from '../../crawl/recheck';
import { liveStore } from '../../live/liveStore';
import { TEST_CRAWL_ID, fakeSidecar, type FakeSidecar } from '../../test/fakeSidecar';
import { recheckEvent, recheckRoutes } from '../../test/fixLoopSidecar';
import { message } from '../../test/overviewSidecar';
import { Button } from '../../ui/Button';
import { DropdownMenu } from '../../ui/Menu';
import { RecheckMenuItem } from './RecheckMenuItem';

interface Row {
  id: number;
}

function show(rows: (Row | undefined)[], checkId: string | null = null): FakeSidecar {
  const sidecar = fakeSidecar(recheckRoutes());
  setTransport(sidecar);
  render(
    <DropdownMenu open trigger={<Button>Menu</Button>}>
      <RecheckMenuItem<Row>
        crawlId={TEST_CRAWL_ID}
        checkId={checkId}
        count={rows.length}
        rows={() => Promise.resolve(rows)}
        idOf={(row) => row.id}
      />
    </DropdownMenu>,
  );
  return sidecar;
}

const started = (sidecar: FakeSidecar) =>
  sidecar.requests.filter((request) => request.method === 'POST' && request.path.endsWith('/rechecks'));

afterEach(() => {
  resetPendingRechecks();
  liveStore.reset();
  setTransport(fakeSidecar([]));
});

describe('Recheck in a grid’s row menu', () => {
  it('rechecks the selection’s URLs, judged by the check in Issues', async () => {
    const sidecar = show([{ id: 31 }, { id: 40 }, undefined], 'broken-internal-links');
    await userEvent.click(screen.getByRole('menuitem', { name: 'Recheck 3 URLs' }));
    await waitFor(() => expect(started(sidecar)).toHaveLength(1));
    expect(started(sidecar)[0]?.body).toEqual({ checkId: 'broken-internal-links', urlIds: [31, 40] });
  });

  it('only fetches them again in URLs, where no check judges them', async () => {
    const sidecar = show([{ id: 7 }]);
    await userEvent.click(screen.getByRole('menuitem', { name: 'Recheck this URL' }));
    await waitFor(() => expect(started(sidecar)[0]?.body).toEqual({ urlIds: [7] }));
  });

  it('takes the first 1,000 of a bigger selection, and says so', () => {
    show(Array.from({ length: 1_200 }, (_, index) => ({ id: index + 1 })));
    expect(screen.getByRole('menuitem', { name: 'Recheck the first 1,000 of 1,200 URLs' })).toBeVisible();
  });

  it('is disabled while a recheck of the crawl runs', async () => {
    show([{ id: 7 }]);
    liveStore.ingest(message('crawl', { crawlId: TEST_CRAWL_ID, status: 'rechecking' }));
    liveStore.ingest(message('recheck', recheckEvent({ crawlId: TEST_CRAWL_ID })));
    await waitFor(() =>
      expect(screen.getByRole('menuitem', { name: 'Recheck this URL' })).toHaveAttribute('aria-disabled', 'true'),
    );
  });
});
