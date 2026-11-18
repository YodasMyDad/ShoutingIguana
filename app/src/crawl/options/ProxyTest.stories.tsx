import type { Meta, StoryObj } from '@storybook/react-vite';
import { QueryClientProvider } from '@tanstack/react-query';
import { createQueryClient } from '../../app/queryClient';
import { ownSidecar } from '../../test/ScreenStory';
import { json, standardRoutes } from '../../test/fakeSidecar';
import type { ProxyTestOutcome, ProxyTestResult } from '../../api/types';
import { StateRow, StoryFrame, StorySection } from '../../test/StoryFrame';
import { ProxyTest, ProxyTestView, type ProxyTestState } from './ProxyTest';
import { PROXY_TEST_ADDRESS, proxyTestBlocked } from './proxyTestInput';

const meta = { title: 'Crawl settings/Proxy test' } satisfies Meta;
export default meta;
type Story = StoryObj<typeof meta>;

const START = 'https://gardenfurnituresets.co.uk/';

/** The engine's sentence for each outcome, as `ProxyTester` words them. */
const RESULTS: Record<ProxyTestOutcome, ProxyTestResult> = {
  ok: {
    outcome: 'ok',
    status: 200,
    elapsedMs: 182,
    message: 'The proxy answered and the site replied 200 OK in 182 ms.',
  },
  proxyAuthRequired: {
    outcome: 'proxyAuthRequired',
    status: 407,
    elapsedMs: 41,
    message: 'The proxy asks for a user name and password (407).',
  },
  proxyRefused: {
    outcome: 'proxyRefused',
    status: null,
    elapsedMs: 3,
    message: 'Nothing answered at the proxy’s address.',
  },
  proxyUnreachable: {
    outcome: 'proxyUnreachable',
    status: null,
    elapsedMs: 10_004,
    message: 'The proxy didn’t answer within 10 seconds.',
  },
  siteUnreachable: {
    outcome: 'siteUnreachable',
    status: 502,
    elapsedMs: 2_210,
    message: 'The proxy answered 502 Bad Gateway: it usually means the proxy couldn’t reach the site.',
  },
  timeout: {
    outcome: 'timeout',
    status: null,
    elapsedMs: 15_001,
    message: 'The site didn’t reply through the proxy within 15 seconds.',
  },
  tls: {
    outcome: 'tls',
    status: null,
    elapsedMs: 96,
    message: 'A secure connection couldn’t be set up, so nothing was fetched. A certificate may not be trusted.',
  },
  failed: {
    outcome: 'failed',
    status: null,
    elapsedMs: 12,
    message: 'The test didn’t work: An error occurred while sending the request.',
  },
};

function View({
  state,
  proxy = 'http://proxy.example.com:8080',
  start = START,
}: {
  state: ProxyTestState;
  proxy?: string;
  start?: string;
}) {
  return (
    <div className="max-w-(--content-max)">
      <ProxyTestView
        state={state}
        blocked={proxyTestBlocked(proxy)}
        startUrl={start}
        address={PROXY_TEST_ADDRESS}
        onAddressChange={() => undefined}
        onTest={() => undefined}
      />
    </div>
  );
}

/** Every state of the test: ready, under way, each answer the engine gives, an engine error, and why it can't run. */
export const EveryState: Story = {
  render: () => (
    <StoryFrame sheet>
      <StorySection title="Before a test">
        <StateRow label="Ready, with the crawl's start address">
          <View state={{ kind: 'idle' }} />
        </StateRow>
        <StateRow label="Ready, no start address (crawl defaults)">
          <View state={{ kind: 'idle' }} start="" />
        </StateRow>
        <StateRow label="No proxy typed">
          <View state={{ kind: 'idle' }} proxy="" />
        </StateRow>
        <StateRow label="Not a whole address">
          <View state={{ kind: 'idle' }} proxy="proxy.example.com:8080" />
        </StateRow>
        <StateRow label="Testing">
          <View state={{ kind: 'testing' }} />
        </StateRow>
      </StorySection>
      <StorySection title="Answers">
        {Object.values(RESULTS).map((result) => (
          <StateRow key={result.outcome} label={result.outcome}>
            <View state={{ kind: 'result', result }} />
          </StateRow>
        ))}
        <StateRow label="The engine couldn't be asked">
          <View state={{ kind: 'error', message: 'The engine isn’t running.' }} />
        </StateRow>
      </StorySection>
    </StoryFrame>
  ),
};

/** A long proxy result and start address in a narrow panel: they wrap, nothing overprints. */
export const Narrow: Story = {
  render: () => (
    <StoryFrame>
      <div className="w-(--settings-section-narrow)">
        <View
          state={{ kind: 'result', result: RESULTS.tls }}
          start={`https://${'very-long-subdomain.'.repeat(4)}example.com/a/long/path/to/start/from`}
        />
      </div>
    </StoryFrame>
  ),
};

/** Live, against a fake engine that answers as a working proxy would: press Test the proxy. */
export const Live: Story = {
  loaders: [() => ownSidecar([['POST', /^\/api\/proxy\/test$/, () => json(RESULTS.ok)], ...standardRoutes()])],
  render: () => (
    <QueryClientProvider client={createQueryClient()}>
      <StoryFrame>
        <div className="max-w-(--content-max)">
          <ProxyTest proxyUrl="http://user:secret@proxy.example.com:8080" startUrl={null} />
        </div>
      </StoryFrame>
    </QueryClientProvider>
  ),
};
