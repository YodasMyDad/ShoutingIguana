import { QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it } from 'vitest';
import { setTransport } from '../../api/transport';
import { fakeSidecar, json, problem } from '../../test/fakeSidecar';
import { createQueryClientWithoutBackoff } from '../../test/queryClient';
import { ProxyTest } from './ProxyTest';
import { proxyTestBlocked } from './proxyTestInput';

function renderTest(proxyUrl: string, startUrl: string | null, answer: () => Response | Promise<Response>) {
  const sidecar = fakeSidecar([['POST', /^\/api\/proxy\/test$/, () => answer()]]);
  setTransport(sidecar);
  const view = render(
    <QueryClientProvider client={createQueryClientWithoutBackoff()}>
      <ProxyTest proxyUrl={proxyUrl} startUrl={startUrl} />
    </QueryClientProvider>,
  );
  return { sidecar, ...view };
}

const OK = {
  outcome: 'ok',
  status: 200,
  elapsedMs: 182,
  message: 'The proxy answered and the site replied 200 OK in 182 ms.',
};

describe('ProxyTest', () => {
  afterEach(() => setTransport(fakeSidecar([])));

  it('tests the proxy as typed, credentials included, against the crawl’s start address', async () => {
    const { sidecar } = renderTest('http://me:pa55@proxy.example.com:8080 ', 'example.com', () => json(OK));
    expect(screen.getByText('It fetches https://example.com through the proxy.')).toBeInTheDocument();
    expect(screen.queryByLabelText('Address to test with')).toBeNull();

    await userEvent.click(screen.getByRole('button', { name: 'Test the proxy' }));

    expect(await screen.findByRole('status')).toHaveTextContent(OK.message);
    expect(sidecar.requests.at(-1)?.body).toEqual({
      proxyUrl: 'http://me:pa55@proxy.example.com:8080',
      url: 'https://example.com',
    });
  });

  it('asks for an address to test with when there is no start address, and drops a result once it changes', async () => {
    const { sidecar } = renderTest('socks5://127.0.0.1:1080', null, () => json(OK));
    const address = screen.getByLabelText('Address to test with');
    expect(address).toHaveValue('https://example.com/');

    await userEvent.click(screen.getByRole('button', { name: 'Test the proxy' }));
    await waitFor(() => expect(screen.getByRole('status')).toHaveTextContent(OK.message));
    expect(sidecar.requests.at(-1)?.body).toEqual({ proxyUrl: 'socks5://127.0.0.1:1080', url: 'https://example.com/' });

    await userEvent.type(address, 'page');
    expect(screen.getByRole('status')).toHaveTextContent('');

    await userEvent.clear(address);
    expect(screen.getByRole('button', { name: 'Test the proxy' })).toBeDisabled();
    expect(screen.getByText('Type an address to test with.')).toBeInTheDocument();
  });

  it('says what the engine said when the test can’t be made', async () => {
    renderTest('http://proxy.example.com:8080', 'https://example.com/', () =>
      problem({ type: 'bad-request', title: 'The proxy must be a whole address.', status: 400 }),
    );

    await userEvent.click(screen.getByRole('button', { name: 'Test the proxy' }));

    expect(await screen.findByText('The proxy must be a whole address.')).toBeInTheDocument();
  });

  it('can’t be pressed without a whole proxy address, and says why', () => {
    renderTest('', 'https://example.com/', () => json(OK));
    const button = screen.getByRole('button', { name: 'Test the proxy' });
    expect(button).toBeDisabled();
    expect(button).toHaveAccessibleDescription('Type a proxy’s address to test it.');

    expect(proxyTestBlocked('proxy.example.com:8080')).toMatch(/whole address/);
    expect(proxyTestBlocked('ftp://proxy.example.com/')).toMatch(/whole address/);
    expect(proxyTestBlocked('http://proxy.example.com:8080')).toBeNull();
    expect(proxyTestBlocked('https://u:p@proxy.example.com')).toBeNull();
  });
});
