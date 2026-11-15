// The proxy test (gap 18 of step 3's closing audit): one fetch through the proxy as typed, its user name and password
// included, and what happened in the engine's plain words (`POST /api/proxy/test`). Beside the proxy field wherever it
// is: New crawl, Settings › Crawl defaults and a crawl's own settings. Advanced only, as the proxy field is.
import { useMutation } from '@tanstack/react-query';
import clsx from 'clsx';
import { CircleCheck, CircleX, PlugZap, TriangleAlert } from 'lucide-react';
import { useId, useState } from 'react';
import { errorTitle } from '../../api/errors';
import { proxyTestMutation } from '../../api/queries';
import type { ProxyTestOutcome, ProxyTestResult } from '../../api/types';
import { Button } from '../../ui/Button';
import { Icon } from '../../ui/Icon';
import { TextField } from '../../ui/TextField';
import { withScheme } from './form';
import { PROXY_TEST_ADDRESS, proxyTestBlocked } from './proxyTestInput';

/** What the test line shows: nothing yet, a test under way, the engine's answer, or why there's none. */
export type ProxyTestState =
  | { kind: 'idle' }
  | { kind: 'testing' }
  | { kind: 'result'; result: ProxyTestResult }
  | { kind: 'error'; message: string };

const OUTCOME_LOOK: Record<ProxyTestOutcome, { icon: typeof CircleCheck; className: string }> = {
  ok: { icon: CircleCheck, className: 'text-text' },
  proxyAuthRequired: { icon: CircleX, className: 'text-error-text' },
  proxyRefused: { icon: CircleX, className: 'text-error-text' },
  proxyUnreachable: { icon: CircleX, className: 'text-error-text' },
  tls: { icon: CircleX, className: 'text-error-text' },
  failed: { icon: CircleX, className: 'text-error-text' },
  siteUnreachable: { icon: TriangleAlert, className: 'text-warning-text' },
  timeout: { icon: TriangleAlert, className: 'text-warning-text' },
};

export interface ProxyTestViewProps {
  state: ProxyTestState;
  /** Why the test can't run now (ProxyTestBlocked), shown beside the button; null when it can. */
  blocked: string | null;
  /** The crawl's start address, fetched through the proxy; empty when there's none and `address` is asked for. */
  startUrl: string;
  address: string;
  onAddressChange: (address: string) => void;
  onTest: () => void;
}

/** The test's look, for every state: the stories and the live component draw the same. */
export function ProxyTestView({ state, blocked, startUrl, address, onAddressChange, onTest }: ProxyTestViewProps) {
  const noteId = useId();
  const testing = state.kind === 'testing';
  const note = blocked ?? (startUrl !== '' ? `It fetches ${startUrl} through the proxy.` : null);
  return (
    <div data-proxy-test="" className="flex flex-col gap-2">
      {startUrl === '' ? (
        <TextField
          label="Address to test with"
          description="Fetched once through the proxy. Nothing is saved."
          mono
          spellCheck={false}
          value={address}
          onChange={(event) => onAddressChange(event.target.value)}
        />
      ) : null}
      <div className="flex flex-wrap items-center gap-x-3 gap-y-1">
        <Button
          size="sm"
          icon={PlugZap}
          loading={testing}
          disabled={blocked !== null}
          aria-describedby={note ? noteId : undefined}
          onClick={onTest}
        >
          Test the proxy
        </Button>
        {note ? (
          <p id={noteId} className="min-w-0 text-12 break-words text-text-muted">
            {note}
          </p>
        ) : null}
      </div>
      {/* There from the start, so a result is announced once, as it arrives; a test under way is the button's. */}
      <p role="status" className="flex min-w-0 items-start gap-2 text-13">
        {state.kind === 'result' ? (
          <>
            <span className={clsx('mt-0.5 flex shrink-0', OUTCOME_LOOK[state.result.outcome].className)}>
              <Icon icon={OUTCOME_LOOK[state.result.outcome].icon} size="sm" />
            </span>
            <span className="min-w-0 break-words text-text">{state.result.message}</span>
          </>
        ) : state.kind === 'error' ? (
          <>
            <span className="mt-0.5 flex shrink-0 text-error-text">
              <Icon icon={CircleX} size="sm" />
            </span>
            <span className="min-w-0 break-words text-text">{state.message}</span>
          </>
        ) : null}
      </p>
    </div>
  );
}

export interface ProxyTestProps {
  /** The proxy as typed, its user name and password included: they go to the engine for this one fetch only. */
  proxyUrl: string;
  /** The crawl's start address, when it has one: fetched through the proxy. Otherwise the user gives one. */
  startUrl?: string | null | undefined;
}

/** The test, live: a press tests the proxy as typed now; a result for a proxy or address since changed is dropped. */
export function ProxyTest({ proxyUrl, startUrl }: ProxyTestProps) {
  const start = startUrl?.trim() ? withScheme(startUrl.trim()) : '';
  const [address, setAddress] = useState(PROXY_TEST_ADDRESS);
  const target = start !== '' ? start : address.trim();
  const test = useMutation(proxyTestMutation());
  const tested = test.variables;
  const current = tested?.proxyUrl === proxyUrl.trim() && tested.url === target;
  const blocked =
    proxyTestBlocked(proxyUrl) ?? (start === '' && target === '' ? 'Type an address to test with.' : null);
  const state: ProxyTestState = test.isPending
    ? { kind: 'testing' }
    : !current
      ? { kind: 'idle' }
      : test.isSuccess
        ? { kind: 'result', result: test.data }
        : test.isError
          ? { kind: 'error', message: errorTitle(test.error) }
          : { kind: 'idle' };
  return (
    <ProxyTestView
      state={state}
      blocked={blocked}
      startUrl={start}
      address={address}
      onAddressChange={setAddress}
      onTest={() => test.mutate({ proxyUrl: proxyUrl.trim(), url: target })}
    />
  );
}
