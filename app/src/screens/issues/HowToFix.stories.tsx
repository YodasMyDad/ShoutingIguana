import type { Meta, StoryObj } from '@storybook/react-vite';
import type { Check } from '../../api/types';
import { SIMPLE_CHECKS } from '../../test/simpleSidecar';
import { StoryFrame } from '../../test/StoryFrame';
import { Card, CardBody, CardHeader } from '../../ui/Card';
import { SeverityBadge } from '../../ui/SeverityBadge';
import { HowToFix } from './HowToFix';

const meta = { title: 'Screens/Issues how to fix' } satisfies Meta;
export default meta;
type Story = StoryObj<typeof meta>;

/** A check as the engine's catalog has it: the captured `GET /api/checks`, so the stories show the real words. */
const check = (id: string): Check => {
  const found = SIMPLE_CHECKS.checks.find((candidate) => candidate.id === id);
  if (!found) throw new Error(`No check ${id} in the captured catalog`);
  return found;
};

/**
 * The owner's case: "Internal links without anchor text", their logo an inline SVG in a link on every page. Three
 * cases, the one the crawl points to showing first and marked "On this site".
 */
export const EmptyAnchorText: Story = {
  render: () => (
    <StoryFrame sheet>
      <HowToFix check={check('empty-anchor-text')} preferredCase="icon-link" />
    </StoryFrame>
  ),
};

/** The same check where the crawl found the links around images: that case shows first. */
export const EmptyAnchorTextImageLinks: Story = {
  render: () => (
    <StoryFrame sheet>
      <HowToFix check={check('empty-anchor-text')} preferredCase="image-link" />
    </StoryFrame>
  ),
};

/** One example: the code under the words, nothing to choose. */
export const OneExample: Story = {
  render: () => (
    <StoryFrame sheet>
      <HowToFix check={check('description-missing')} />
      <HowToFix check={check('image-missing-dimensions')} />
    </StoryFrame>
  ),
};

/** A lone example whose fix is conditional leads with its condition. */
export const OneConditionalExample: Story = {
  render: () => (
    <StoryFrame sheet>
      <HowToFix check={check('redirect-temporary-cached')} />
      <HowToFix check={check('blocked-by-robots')} />
    </StoryFrame>
  ),
};

/** Cases with nothing to say which the site has: the first shows, none is marked. Headers, and JSON-LD. */
export const CasesWithoutAPreference: Story = {
  render: () => (
    <StoryFrame sheet>
      <HowToFix check={check('security-headers-weak')} />
      <HowToFix check={check('structured-data-schema-errors')} />
    </StoryFrame>
  ),
};

/** An editorial fix: the words alone, as before. */
export const ProseOnly: Story = {
  render: () => (
    <StoryFrame sheet>
      <HowToFix check={check('title-too-long')} />
    </StoryFrame>
  ),
};

/**
 * Where it will live: in a check's pane, beside "What's wrong" and "Why it matters" where there's room and under them
 * where there isn't. (The pane itself mounts it once the Issues lane has landed; this is the block in that setting.)
 */
export const InACheck: Story = {
  render: () => {
    const emptyLink = check('empty-anchor-text');
    return (
      <StoryFrame>
        <Card as="section" aria-labelledby="in-a-check-title">
          <CardHeader
            title={emptyLink.title}
            titleId="in-a-check-title"
            meta={<SeverityBadge severity={emptyLink.severity} />}
          />
          <CardBody>
            <div className="grid gap-x-10 gap-y-6 wide:grid-cols-(--how-to-fix-columns)">
              <div className="flex flex-col gap-5">
                <div>
                  <h3 className="mb-1.5 text-13 font-semibold">What’s wrong</h3>
                  <p className="max-w-(--measure-text) text-14 leading-body text-text-secondary">
                    {emptyLink.description}
                  </p>
                </div>
                <div>
                  <h3 className="mb-1.5 text-13 font-semibold">Why it matters</h3>
                  <p className="max-w-(--measure-text) text-14 leading-body text-text-secondary">{emptyLink.why}</p>
                </div>
              </div>
              <HowToFix check={emptyLink} preferredCase="icon-link" />
            </div>
          </CardBody>
        </Card>
      </StoryFrame>
    );
  },
};
