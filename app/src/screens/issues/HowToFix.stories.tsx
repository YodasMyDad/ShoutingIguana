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

/** The words, for a check whose fix used to be shown as code and for one that was always words. */
export const Words: Story = {
  render: () => (
    <StoryFrame sheet>
      <HowToFix check={check('empty-anchor-text')} />
      <HowToFix check={check('title-too-long')} />
    </StoryFrame>
  ),
};

/**
 * Where it lives: in a check's head, beside what's wrong and why it matters where there's room, and under them where
 * there isn't.
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
            <div className="grid gap-x-10 gap-y-6 wide:grid-cols-(--explain-columns)">
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
              <HowToFix check={emptyLink} />
            </div>
          </CardBody>
        </Card>
      </StoryFrame>
    );
  },
};
