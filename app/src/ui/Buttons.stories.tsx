import type { Meta, StoryObj } from '@storybook/react-vite';
import { Copy, Download, MoreHorizontal, Pause, RefreshCw, Square, Trash2, X } from 'lucide-react';
import { useEffect } from 'react';
import { commands } from '../keyboard/registry';
import { StateRow, StoryFrame, StorySection, StoryRouter } from '../test/StoryFrame';
import { Button } from './Button';
import { IconButton } from './IconButton';
import { Kbd } from './Kbd';
import { Spinner } from './Spinner';
import { Tooltip } from './Tooltip';

const meta = { title: 'Design system/Buttons' } satisfies Meta;
export default meta;
type Story = StoryObj<typeof meta>;

/** The crawl's keys, as the app registers them, so tooltips and keycaps show F7 and F8. */
function useCrawlKeys() {
  useEffect(
    () =>
      commands.register([
        { id: 'story.pause', title: 'Pause the crawl', group: 'Crawl', keys: ['f7'], run: () => undefined },
        { id: 'story.stop', title: 'Stop the crawl', group: 'Crawl', keys: ['f8'], run: () => undefined },
        { id: 'story.copy', title: 'Copy the URLs', group: 'View', keys: ['mod+shift+c'], run: () => undefined },
      ]),
    [],
  );
}

const VARIANTS = ['primary', 'secondary', 'ghost', 'danger'] as const;
const SIZES = ['sm', 'md', 'lg', 'xl'] as const;

/** Every variant at every size, with and without an icon. */
export const Variants: Story = {
  render: () => (
    <StoryFrame sheet>
      {VARIANTS.map((variant) => (
        <StorySection key={variant} title={variant}>
          {SIZES.map((size) => (
            <StateRow key={size} label={size}>
              <Button variant={variant} size={size}>
                {variant === 'danger' ? 'Delete crawl file' : 'Crawl again'}
              </Button>
              <Button variant={variant} size={size} icon={variant === 'danger' ? Trash2 : RefreshCw}>
                {variant === 'danger' ? 'Delete crawl file' : 'Crawl again'}
              </Button>
              <Button variant={variant} size={size} iconEnd={Download}>
                Export
              </Button>
            </StateRow>
          ))}
        </StorySection>
      ))}
    </StoryFrame>
  ),
};

/**
 * Each state, forced so it can be reviewed without touching: hover, focus (the keyboard's ring), pressed with the
 * pointer, a toggle that's on, disabled, and working (the spinner shows after 300 ms, the width stays).
 */
export const States: Story = {
  parameters: {
    pseudo: {
      hover: ['.is-hover'],
      focusVisible: ['.is-focus'],
      active: ['.is-active'],
    },
  },
  render: () => (
    <StoryFrame sheet>
      {VARIANTS.map((variant) => (
        <StorySection key={variant} title={variant}>
          <StateRow label="Default · hover · focus · active">
            <Button variant={variant}>Default</Button>
            <Button variant={variant} className="is-hover">
              Hover
            </Button>
            <Button variant={variant} className="is-focus">
              Focus
            </Button>
            <Button variant={variant} className="is-active">
              Active
            </Button>
          </StateRow>
          <StateRow label="Disabled · loading">
            <Button variant={variant} disabled>
              Disabled
            </Button>
            <Button variant={variant} loading>
              Analyse again
            </Button>
            <Button variant={variant} icon={RefreshCw} loading>
              Crawl again
            </Button>
          </StateRow>
        </StorySection>
      ))}
      <StorySection title="Toggle (pressed)">
        <StateRow label="Off · on · on, focus">
          <Button pressed={false} icon={Pause}>
            Show passed
          </Button>
          <Button pressed icon={Pause}>
            Show passed
          </Button>
          <Button pressed variant="ghost" icon={Pause} className="is-focus">
            Show passed
          </Button>
        </StateRow>
      </StorySection>
    </StoryFrame>
  ),
};

/** Keys in buttons and tooltips, from the registry: Pause F7, Stop F8 (docs/v2-ui-design.md, "the keyboard on the surface"). */
export const WithShortcuts: Story = {
  render: function WithShortcuts() {
    useCrawlKeys();
    return (
      <StoryFrame sheet>
        <StateRow label="Keycaps inside">
          <Button icon={Pause} command="story.pause" showShortcut>
            Pause
          </Button>
          <Button icon={Square} command="story.stop" showShortcut>
            Stop
          </Button>
          <Button variant="primary" command="story.copy" showShortcut icon={Copy}>
            Copy URLs
          </Button>
        </StateRow>
        <StateRow label="Keycaps alone">
          <Kbd chord="mod+k" />
          <Kbd chord="f7" />
          <Kbd chord="mod+shift+c" size="sm" />
        </StateRow>
      </StoryFrame>
    );
  },
};

/** Icon-only buttons: each has a name, which is its tooltip. */
export const IconButtons: Story = {
  parameters: { pseudo: { hover: ['.is-hover'], focusVisible: ['.is-focus'] } },
  render: () => (
    <StoryFrame sheet>
      {(['xs', 'sm', 'md', 'lg'] as const).map((size) => (
        <StateRow key={size} label={size}>
          <IconButton label="More actions" icon={MoreHorizontal} size={size} />
          <IconButton label="More actions" icon={MoreHorizontal} size={size} className="is-hover" />
          <IconButton label="More actions" icon={MoreHorizontal} size={size} className="is-focus" />
          <IconButton label="Close" icon={X} size={size} disabled />
          <IconButton label="Copy" icon={Copy} size={size} variant="secondary" />
          <IconButton label="Hide passed checks" icon={Pause} size={size} pressed />
        </StateRow>
      ))}
    </StoryFrame>
  ),
};

/** A tooltip, held open: the label, then the command's keys. */
export const TooltipOpen: Story = {
  render: function TooltipOpen() {
    useCrawlKeys();
    return (
      <StoryFrame sheet>
        <div className="flex h-(--chart-height) items-start gap-16 pt-4">
          <Tooltip content="Pause the crawl" command="story.pause" open>
            <Button icon={Pause}>Pause</Button>
          </Tooltip>
          <Tooltip content="https://www.gardenfurnituresets.co.uk/harpswell-metal-patio-set/" hint="Click to copy" open>
            <Button variant="ghost">A long tooltip</Button>
          </Tooltip>
        </div>
      </StoryFrame>
    );
  },
};

/** Long labels don't wrap; a working button keeps its width. */
export const LongTextAndLinks: Story = {
  render: () => (
    <StoryRouter>
      <StoryFrame sheet>
        <StateRow label="Long label">
          <Button icon={Download}>Export all 85 checks as one workbook</Button>
        </StateRow>
        <StateRow label="A link, as a button">
          <Button asChild variant="secondary">
            <a href="#home">Go to Home</a>
          </Button>
          <Button asChild variant="primary" icon={RefreshCw}>
            <a href="#crawl">Crawl again</a>
          </Button>
        </StateRow>
        <StateRow label="Spinners">
          <Spinner size="xs" />
          <Spinner size="sm" />
          <Spinner size="md" />
          <Spinner size="lg" label="Loading the library" />
        </StateRow>
      </StoryFrame>
    </StoryRouter>
  ),
};
