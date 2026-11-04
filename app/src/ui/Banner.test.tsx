import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { Banner } from './Banner';
import { Callout } from './Callout';

// docs/v2-ui-design.md: severity's colours and shapes mean a finding's severity, and nothing else.
describe('the app’s messages', () => {
  it('banners and callouts about the app never take the severity marks', () => {
    const { container } = render(
      <>
        <Banner tone="warning">The engine stopped unexpectedly. Restarting it (2 of 3)…</Banner>
        <Banner tone="error">The engine keeps stopping.</Banner>
        <Callout tone="warning" title="3 checks were skipped">
          No sitemap could be read.
        </Callout>
        <Callout tone="error" title="robots.txt couldn’t be read">
          The server answered 503 three times.
        </Callout>
      </>,
    );
    expect(container.querySelectorAll('.si-sev-mark')).toHaveLength(0);
    // Each has its message icon instead, beside its words.
    expect(container.querySelectorAll('.si-banner__icon svg, .si-callout__icon svg')).toHaveLength(4);
    expect(screen.getByRole('alert')).toHaveTextContent('The engine keeps stopping.');
  });
});
