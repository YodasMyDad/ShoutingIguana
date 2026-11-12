import type { Meta, StoryObj } from '@storybook/react-vite';
import { useState } from 'react';
import { StateRow, StoryFrame, StorySection } from '../test/StoryFrame';
import { ChoiceCards } from './ChoiceCards';
import { Fold } from './Fold';
import { Switch } from './Switch';

const meta = { title: 'Design system/Choices and folds' } satisfies Meta;
export default meta;
type Story = StoryObj<typeof meta>;

const SPEEDS = [
  { value: 'polite', title: 'Polite', text: 'Up to 2 addresses a second. For small or shared hosting.' },
  { value: 'standard', title: 'Standard', text: '5 addresses at a time. Right for most sites.' },
  { value: 'fast', title: 'Fast', text: '16 addresses at a time. For big sites on strong hosting.' },
] as const;

function Cards({ initial, columns }: { initial: string | null; columns?: 2 | 3 }) {
  const [value, setValue] = useState(initial);
  return (
    <ChoiceCards
      legend="How fast"
      description="Faster checks finish sooner but ask more of the site’s hosting."
      options={columns === 2 ? SPEEDS.slice(0, 2) : SPEEDS}
      value={value}
      onValueChange={setValue}
      columns={columns}
    />
  );
}

function OneFold({ open: initial, attention }: { open: boolean; attention?: string }) {
  const [open, setOpen] = useState(initial);
  return (
    <div className="si-card">
      <Fold
        title="Scope"
        summary="This folder only, no subdomains, nofollow links followed, canonicals not followed, hreflang followed"
        open={open}
        onOpenChange={setOpen}
        attention={attention}
      >
        <Switch
          label="Stay in the start folder"
          description="Crawl only URLs under the start URL’s folder."
          defaultChecked
        />
      </Fold>
      <Fold
        title="Limits"
        summary="No URL limit, any depth, HTML up to 8 MB"
        open={false}
        onOpenChange={() => undefined}
      >
        {null}
      </Fold>
    </div>
  );
}

/** Choice cards: one of a few, each with a line on what it means. */
export const Cards_: Story = {
  name: 'Choice cards',
  render: () => (
    <StoryFrame>
      <StorySection title="Choice cards">
        <StateRow label="One chosen">
          <Cards initial="standard" />
        </StateRow>
        <StateRow label="None chosen (the user’s own, set elsewhere)">
          <Cards initial={null} />
        </StateRow>
        <StateRow label="Two a row">
          <Cards initial="polite" columns={2} />
        </StateRow>
      </StorySection>
    </StoryFrame>
  ),
};

/** Folded sections of a form: the title and its values in a line, open, and saying something needs a look. */
export const Folds: Story = {
  render: () => (
    <StoryFrame>
      <StorySection title="Folds">
        <StateRow label="Folded">
          <OneFold open={false} />
        </StateRow>
        <StateRow label="Open">
          <OneFold open />
        </StateRow>
        <StateRow label="Something to change">
          <OneFold open={false} attention="1 to change" />
        </StateRow>
      </StorySection>
    </StoryFrame>
  ),
};
