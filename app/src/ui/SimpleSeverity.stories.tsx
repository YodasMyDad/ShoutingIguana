import type { Meta, StoryObj } from '@storybook/react-vite';
import type { AnchorHTMLAttributes } from 'react';
import { OnSurfaces, StateRow, StoryFrame, StorySection } from '../test/StoryFrame';
import { OneFixTag, PlainCheckList, PlainCheckRow } from './PlainCheckRow';
import { SeverityWord } from './SeverityWord';
import { plainSeverityCount, SEVERITIES } from './severity';

/** A link whose words the row puts inside it. */
function Anchor({ children, ...props }: AnchorHTMLAttributes<HTMLAnchorElement>) {
  return <a {...props}>{children}</a>;
}

const meta = { title: 'Design system/Simple severity and check rows' } satisfies Meta;
export default meta;
type Story = StoryObj<typeof meta>;

const OUTCOMES = ['error', 'warning', 'notice', 'skipped', 'passed'] as const;

/**
 * Severity in Simple's words: Serious, Worth fixing and Worth a look, with Error's, Warning's and Notice's shapes and
 * colours, one to one; Couldn't check and Nothing wrong for a skipped or passed check.
 */
export const SeverityWords: Story = {
  render: () => (
    <StoryFrame sheet>
      <StateRow label="Badge">
        {OUTCOMES.map((outcome) => (
          <SeverityWord key={outcome} severity={outcome} />
        ))}
      </StateRow>
      <StateRow label="Large">
        {OUTCOMES.map((outcome) => (
          <SeverityWord key={outcome} severity={outcome} variant="large" />
        ))}
      </StateRow>
      <StateRow label="Inline">
        {OUTCOMES.map((outcome) => (
          <SeverityWord key={outcome} severity={outcome} variant="inline" />
        ))}
      </StateRow>
      <StateRow label="Mark, named">
        {OUTCOMES.map((outcome) => (
          <SeverityWord key={outcome} severity={outcome} variant="mark" />
        ))}
      </StateRow>
      <StateRow label="Counts">
        {SEVERITIES.map((severity) => (
          <span key={severity}>
            {plainSeverityCount(severity, 1)}; {plainSeverityCount(severity, 13)}
          </span>
        ))}
      </StateRow>
      <StorySection title="On each surface">
        <OnSurfaces>
          <span className="flex flex-wrap gap-2">
            {OUTCOMES.map((outcome) => (
              <SeverityWord key={outcome} severity={outcome} />
            ))}
          </span>
        </OnSurfaces>
      </StorySection>
    </StoryFrame>
  ),
};

const LONG_TITLE =
  'Pages whose search description is far too long to be shown in full, which happens on very large shops with long names';
const LONG_SUMMARY =
  'Some addresses on your site answer with an error instead of a page or file, most often "not found" (a 404), which visitors meet as an error page.';

/**
 * A check in Simple: its mark named in Simple's word, the plain title, "One fix" when one cause covers most of it, the
 * first sentence of what's wrong, and its count and share. As a link it has a chevron, and every state of a link.
 */
export const CheckRows: Story = {
  parameters: { pseudo: { hover: ['.is-hover'], focusVisible: ['.is-focus'] } },
  render: () => (
    <StoryFrame>
      <div className="si-card overflow-hidden">
        <PlainCheckList label="Rows in every state">
          <li>
            <PlainCheckRow
              asChild
              number={1}
              outcome="error"
              title="Pages or files that can’t be found"
              summary={LONG_SUMMARY}
              count={
                <>
                  <b>9</b> of 4,521 addresses
                </>
              }
            >
              <Anchor href="#problem" />
            </PlainCheckRow>
          </li>
          <li>
            <PlainCheckRow
              asChild
              outcome="warning"
              title="Large images that slow pages down"
              summary="Some pages show images whose files are over 200 KB."
              count={
                <>
                  <b>61</b> of 265 pages
                </>
              }
              className="is-hover"
            >
              <Anchor href="#hover" />
            </PlainCheckRow>
          </li>
          <li>
            <PlainCheckRow
              asChild
              outcome="notice"
              quiet
              oneFix
              title="Links with nothing to read"
              summary="Some links on your pages have no words: usually an image or icon, such as a logo, with no description."
              count={
                <>
                  <b>265</b> of 265 pages
                </>
              }
              className="is-focus"
            >
              <Anchor href="#focus" />
            </PlainCheckRow>
          </li>
          <li>
            <PlainCheckRow
              asChild
              outcome="notice"
              quiet
              title="Images that make the page jump as it loads"
              summary="Some images don’t tell the browser their width and height."
              count={
                <>
                  <b>230</b> of 265 pages
                </>
              }
              aria-current="true"
            >
              <Anchor href="#current" />
            </PlainCheckRow>
          </li>
          <li>
            <PlainCheckRow
              asChild
              outcome="error"
              title={LONG_TITLE}
              summary={LONG_SUMMARY}
              count={
                <>
                  <b>104,233</b> of 110,251 addresses
                </>
              }
            >
              <Anchor href="#long" />
            </PlainCheckRow>
          </li>
          <li>
            <PlainCheckRow
              asChild
              outcome="skipped"
              quiet
              title="Your site answers at more than one address"
              summary="The site’s other addresses (with and without www, http and https) couldn’t all be tried. Check the site again to include it."
              count="Not checked"
            >
              <Anchor href="#skipped" />
            </PlainCheckRow>
          </li>
          <li>
            <PlainCheckRow outcome="passed" quiet title="Every page has a title" />
          </li>
        </PlainCheckList>
      </div>
      <StateRow label="One fix">
        <OneFixTag />
      </StateRow>
    </StoryFrame>
  ),
};
