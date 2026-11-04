import { createLink } from '@tanstack/react-router';
import clsx from 'clsx';
import type { AnchorHTMLAttributes, Ref } from 'react';

type TextAnchorProps = AnchorHTMLAttributes<HTMLAnchorElement> & {
  ref?: Ref<HTMLAnchorElement> | undefined;
  /**
   * `standalone` (the default): the accent, underlined on hover ("Show all 264 pages in URLs"). `inline`: in running
   * text, always underlined, so it's never told by colour alone. `subtle`: the text's colour, the accent on hover (a
   * site's name in a table).
   */
  variant?: 'standalone' | 'inline' | 'subtle' | undefined;
};

function TextAnchor({ ref, variant = 'standalone', className, children, ...rest }: TextAnchorProps) {
  return (
    <a
      ref={ref}
      {...rest}
      className={clsx(
        'si-link',
        variant === 'inline' && 'si-link--inline',
        variant === 'subtle' && 'si-link--subtle',
        className,
      )}
    >
      {children}
    </a>
  );
}

/** A link to another view, in the app's look: the router's `Link` (typed `to`, `params`, `search`) with a variant. */
export const TextLink = createLink(TextAnchor);
