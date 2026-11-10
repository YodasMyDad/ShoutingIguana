import { createLink, type LinkProps } from '@tanstack/react-router';
import type { AnchorHTMLAttributes, Ref } from 'react';

function ChartAnchor({ ref, ...rest }: AnchorHTMLAttributes<HTMLAnchorElement> & { ref?: Ref<HTMLAnchorElement> }) {
  // eslint-disable-next-line jsx-a11y-x/anchor-has-content -- the part it wraps, or its aria-label, is its content
  return <a ref={ref} {...rest} />;
}

/**
 * A chart's part that opens its URLs ("every number and chart part is a link"): the router's `Link` with no look of
 * its own. The chart gives it its class and its name.
 */
export const ChartLink = createLink(ChartAnchor);

/** Where a chart's part leads: the router's `to`, `params` and `search`. */
export type ChartPartLink = LinkProps;
