import type { Meta, StoryObj } from '@storybook/react-vite';
import { useState } from 'react';
import { ErrorState } from '../feedback/ErrorState';
import { Card } from '../ui/Card';
import { SourcePane } from './SourcePane';
import type { SourceLineMark } from './sourceLines';
import { useSourceViewer } from './sourceViewerLoader';

const meta = { title: 'Design system/Source pane' } satisfies Meta;
export default meta;
type Story = StoryObj<typeof meta>;

const FILE = [
  '# Lines the viewer marks, in each way it can',
  'User-agent: *',
  'Disallow: /',
  '',
  'User-agent: ShoutingIguana',
  'Disallow: /admin/',
  'Disallow: /cart',
  `Disallow: /${'a-very-long-path-segment/'.repeat(12)}*?sessionid=`,
  'Crawl-delay: soon',
  'Host: example.com',
  'Sitemap: https://example.com/sitemap.xml',
].join('\n');

const MARKS: SourceLineMark[] = [
  { line: 1, comment: true },
  { line: 2, tone: 'quiet', nameEnd: 10 },
  { line: 3, tone: 'quiet', nameEnd: 8 },
  { line: 5, tone: 'strong', nameEnd: 10 },
  {
    line: 6,
    tone: 'strong',
    nameEnd: 8,
    action: 'Blocked 1,204 URLs',
    actionTitle: 'List the 1,204 URLs line 6 blocked',
  },
  { line: 7, tone: 'strong', nameEnd: 8 },
  { line: 8, tone: 'strong', nameEnd: 8, action: 'Blocked 3 URLs', actionTitle: 'List the 3 URLs line 8 blocked' },
  { line: 9, tone: 'strong', nameEnd: 11, note: 'Ignored: its value isn’t a number of seconds, 0 or more' },
  { line: 10, nameEnd: 4, note: 'Not used by the crawl' },
  { line: 11, nameEnd: 7 },
];

function Pane({
  chosen: initial,
  text = FILE,
  marks = MARKS,
}: {
  chosen: number | null;
  text?: string;
  marks?: SourceLineMark[];
}) {
  const viewer = useSourceViewer();
  const [chosen, setChosen] = useState(initial);
  const Viewer = viewer.Viewer;
  return (
    <main className="h-screen bg-canvas p-8 text-text">
      <Card as="section" flush aria-label="The file" className="flex h-full flex-col">
        {Viewer ? (
          <SourcePane
            text={text}
            label="A marked text"
            searchLabel="Search the file"
            searchPlaceholder="Search the file"
            Viewer={Viewer}
            language="plain"
            lineMarks={marks}
            chosenLine={chosen}
            onLineAction={setChosen}
            meta={
              chosen === null
                ? 'No line chosen. Press a line’s link, or Enter with the caret on its line.'
                : `Line ${chosen} is chosen.`
            }
          />
        ) : viewer.error ? (
          <ErrorState error={viewer.error} title="The viewer couldn’t be loaded." onRetry={viewer.retry} compact />
        ) : null}
      </Card>
    </main>
  );
}

/**
 * Plain text with marked lines (`sourceLines.ts`): a comment, a quiet group, a strong group with the accent's rule at
 * its edge, a name set apart, a note after a line, and a link that chooses its line. The search, Wrap lines and the
 * caret are the HTML source's.
 */
export const MarkedLines: Story = { render: () => <Pane chosen={null} /> };

/** A line chosen: the selected tint with the accent's rule, its text in the text's own colour. */
export const ChosenLine: Story = { render: () => <Pane chosen={6} /> };

/** A long text: a chosen line far down comes into view, and the marks cost nothing to scroll past. */
export const ThousandsOfLines: Story = {
  render: () => {
    const lines = Array.from({ length: 6_000 }, (_, index) => `Disallow: /catalogue/${index}/private/`);
    const marks = lines.map((_, index): SourceLineMark => ({
      line: index + 1,
      tone: index < 3_000 ? 'quiet' : 'strong',
      nameEnd: 8,
      ...(index >= 3_000 && index % 50 === 0 ? { action: `Blocked ${(index % 400) + 1} URLs` } : {}),
    }));
    return <Pane chosen={4_501} text={lines.join('\n')} marks={marks} />;
  },
};
