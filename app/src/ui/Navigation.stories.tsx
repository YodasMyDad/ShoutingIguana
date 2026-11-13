import type { Meta, StoryObj } from '@storybook/react-vite';
import { Download, FileText, Home, Link2, List, Network, Settings2, TriangleAlert } from 'lucide-react';
import { useState } from 'react';
import { StateRow, StoryFrame, StorySection, StoryRouter } from '../test/StoryFrame';
import { AddFilterChip, FilterChip } from './FilterChip';
import { Button } from './Button';
import { Fact, Facts } from './Facts';
import { Favicon } from './Favicon';
import { SYNTHETIC_FAVICON } from '../api/mock/e6';
import { LiveBadge } from './LiveBadge';
import { ListHeading, ListRow } from './ListRow';
import { NavItem } from './NavItem';
import { Num } from './Num';
import { GridHeader, PageHeader, ResultCount, SectionHeader } from './PageHeader';
import { SearchField } from './SearchField';
import { Badge } from './Badge';
import { SeverityMark } from './SeverityMark';
import { StatusCode } from './StatusCode';
import { TextLink } from './TextLink';
import { Url } from './Url';

/** The synthetic site's icon, as the engine serves it: a 32 px PNG. */
const ICON = `data:image/png;base64,${btoa(String.fromCharCode(...SYNTHETIC_FAVICON))}`;
/** A PNG's signature and nothing after it: an image the browser can't draw. */
const BROKEN_ICON = 'data:image/png;base64,iVBORw0KGgo=';

const meta = { title: 'Design system/Navigation, lists and URLs' } satisfies Meta;
export default meta;
type Story = StoryObj<typeof meta>;

const SITE = 'https://www.gardenfurnituresets.co.uk';
const CRAWL = '0123456789abcdef';

/**
 * URLs: mono, the host muted, truncated in the middle so the host and the end stay. On the crawl's own origin only
 * the path shows; other hosts and the site's other versions show in full. Hover for the full URL and the
 * open-in-browser button; click to copy.
 */
export const Urls: Story = {
  parameters: { pseudo: { hover: ['.is-hover'], focusVisible: ['.is-focus .si-url__copy'] } },
  render: () => (
    <StoryFrame sheet>
      <StorySection title="Widths: 480, 320 and 200 px">
        {(['w-(--dialog-width)', 'w-(--inspector-width-min)', 'w-(--settings-nav-width)'] as const).map((width) => (
          <div key={width} className={`flex flex-col gap-1 ${width}`}>
            <Url url={`${SITE}/harpswell-metal-patio-set-with-6-chairs-and-parasol/?colour=graphite&size=large`} />
            <Url
              url={`${SITE}/harpswell-metal-patio-set-with-6-chairs-and-parasol/?colour=graphite&size=large`}
              siteOrigin={SITE}
            />
            <Url url="https://cdn.shopify.example/s/files/1/0012/3456/products/harpswell-chair-graphite-1200x1200.jpg" />
          </div>
        ))}
      </StorySection>
      <StorySection title="Path only, other versions in full">
        <StateRow label="Own origin">
          <Url url={`${SITE}/frequently-asked-questions/`} siteOrigin={SITE} />
        </StateRow>
        <StateRow label="http version">
          <Url url="http://www.gardenfurnituresets.co.uk/contact-us/" siteOrigin={SITE} />
        </StateRow>
        <StateRow label="Bare domain">
          <Url url="https://gardenfurnituresets.co.uk/" siteOrigin={SITE} />
        </StateRow>
        <StateRow label="Home page">
          <Url url={`${SITE}/`} siteOrigin={SITE} />
        </StateRow>
      </StorySection>
      <StorySection title="States">
        <StateRow label="Hover">
          <Url url={`${SITE}/login/`} className="is-hover" />
        </StateRow>
        <StateRow label="Focus">
          <span className="is-focus">
            <Url url={`${SITE}/login/`} />
          </span>
        </StateRow>
        <StateRow label="Not copyable">
          <Url url={`${SITE}/login/`} copyable={false} openable={false} />
        </StateRow>
        <StateRow label="A link in the app">
          <div className="w-(--inspector-width-min)">
            <Url url={`${SITE}/garden-furniture/`} siteOrigin={SITE} openable={false} onGo={() => undefined} />
          </div>
        </StateRow>
        <StateRow label="A link, hover">
          <div className="w-(--inspector-width-min)">
            <Url
              url={`${SITE}/garden-furniture/`}
              siteOrigin={SITE}
              openable={false}
              onGo={() => undefined}
              className="is-link-hover"
            />
          </div>
        </StateRow>
        <StateRow label="Wrapped">
          <div className="w-(--inspector-width-min)">
            <Url
              truncate="wrap"
              url={`${SITE}/harpswell-metal-patio-set-with-6-chairs-and-parasol/?colour=graphite&size=large`}
            />
          </div>
        </StateRow>
        <StateRow label="In a sentence">
          <p className="max-w-(--content-max) text-13 text-text-secondary">
            Crawling <Url inline url={`${SITE}/`} copyable={false} openable={false} /> at the Polite speed: 2 requests
            at a time.
          </p>
        </StateRow>
      </StorySection>
      <StorySection title="Favicons">
        <StateRow label="sm · md · lg">
          <Favicon site="gardenfurnituresets.co.uk" size="sm" />
          <Favicon site="https://www.kestrel-legal.example/" size="md" />
          <Favicon site="127.0.0.1:50196" size="lg" />
        </StateRow>
        <StateRow label="The site's icon, loaded · sm · md · lg">
          <Favicon site="127.0.0.1:50196" src={ICON} size="sm" />
          <Favicon site="127.0.0.1:50196" src={ICON} size="md" />
          <Favicon site="127.0.0.1:50196" src={ICON} size="lg" />
        </StateRow>
        <StateRow label="An icon that can't be drawn: the letter, the same size">
          <Favicon site="gardenfurnituresets.co.uk" src={BROKEN_ICON} size="md" />
          <Favicon site="gardenfurnituresets.co.uk" src={BROKEN_ICON} size="lg" />
        </StateRow>
      </StorySection>
    </StoryFrame>
  ),
};

/** Links to other views: standalone, inline in running text (always underlined), and subtle. */
export const Links: Story = {
  parameters: { pseudo: { hover: ['.is-hover'], focusVisible: ['.is-focus'] } },
  render: () => (
    <StoryRouter>
      <StoryFrame sheet>
        <StateRow label="Standalone">
          <TextLink to="/crawls/$crawl/urls" params={{ crawl: CRAWL }}>
            Show all 264 pages in URLs
          </TextLink>
          <TextLink to="/crawls/$crawl/urls" params={{ crawl: CRAWL }} className="is-hover">
            Hovered
          </TextLink>
          <TextLink to="/crawls/$crawl/urls" params={{ crawl: CRAWL }} className="is-focus">
            Focused
          </TextLink>
        </StateRow>
        <StateRow label="Inline">
          <p className="text-13 text-text-secondary">
            Out of date.{' '}
            <TextLink variant="inline" to="/crawls/$crawl/overview" params={{ crawl: CRAWL }}>
              Analyse again
            </TextLink>{' '}
            to bring the issues up to date.
          </p>
        </StateRow>
        <StateRow label="Subtle">
          <TextLink variant="subtle" to="/crawls/$crawl" params={{ crawl: CRAWL }}>
            gardenfurnituresets.co.uk
          </TextLink>
        </StateRow>
      </StoryFrame>
    </StoryRouter>
  ),
};

/**
 * Places in the sidebar: an icon, a label and a count. The current one has the tint, the accent text and the accent
 * rule; hover is the quiet tint. Collapsed, the label is the name and the tooltip.
 */
export const NavItems: Story = {
  parameters: { pseudo: { hover: ['.is-hover'], focusVisible: ['.is-focus'] } },
  render: () => (
    <StoryRouter path={`/crawls/${CRAWL}/issues`}>
      <StoryFrame>
        <div className="flex gap-8">
          <nav aria-label="Main" className="flex w-(--sidebar-width) flex-col gap-0.5">
            <NavItem to="/" label="Home" icon={Home} />
            <NavItem to="/crawls/$crawl/overview" params={{ crawl: CRAWL }} label="Overview" icon={Network} />
            {/* The current place, focused: under forced colours its ring and the focus ring show together. */}
            <NavItem
              to="/crawls/$crawl/issues/{-$check}"
              params={{ crawl: CRAWL }}
              label="Issues"
              icon={TriangleAlert}
              count={19}
              className="is-focus"
            />
            <NavItem
              to="/crawls/$crawl/urls"
              params={{ crawl: CRAWL }}
              label="URLs"
              icon={List}
              count={4521}
              className="is-hover"
            />
            <NavItem
              to="/crawls/$crawl/links"
              params={{ crawl: CRAWL }}
              label="Links"
              icon={Link2}
              count={21_914}
              className="is-focus"
            />
            <NavItem
              to="/crawls/$crawl/sitemaps/{-$sitemap}"
              params={{ crawl: CRAWL }}
              label="Sitemaps"
              icon={FileText}
              count={0}
            />
            <NavItem
              to="/crawls/$crawl/settings"
              params={{ crawl: CRAWL }}
              label="Crawl settings with a long name that truncates"
              icon={Settings2}
            />
          </nav>
          <nav aria-label="Collapsed" className="flex w-(--sidebar-width-collapsed) flex-col gap-0.5">
            <NavItem to="/" label="Home" icon={Home} collapsed />
            <NavItem
              to="/crawls/$crawl/issues/{-$check}"
              params={{ crawl: CRAWL }}
              label="Issues"
              icon={TriangleAlert}
              collapsed
            />
            <NavItem to="/crawls/$crawl/urls" params={{ crawl: CRAWL }} label="URLs" icon={List} collapsed />
          </nav>
        </div>
      </StoryFrame>
    </StoryRouter>
  ),
};

/**
 * List rows, as the checks list uses them: one listbox, a group per category named by its `ListHeading`
 * (`level="none"`, as only options belong in a listbox), the severity mark, the title, the count; selected with the
 * accent rule, and focused as well, so forced colours show its ring beside the focus ring; skipped with its reason and
 * the dashed badge.
 */
export const ListRows: Story = {
  parameters: { pseudo: { hover: ['.is-hover'], focusVisible: ['.is-focus'] } },
  render: function ListRows() {
    const [selected, setSelected] = useState('anchor');
    const rows = [
      { id: '4xx', severity: 'error', title: 'Internal URL returns 4xx', count: 9 },
      { id: 'broken', severity: 'error', title: 'Links to broken internal URLs', count: 5 },
      { id: 'anchor', severity: 'notice', title: 'Internal links without anchor text', count: 265 },
      {
        id: 'long',
        severity: 'warning',
        title: 'Another version of the site answers with a page instead of redirecting to the main one',
        count: 82_904,
      },
    ] as const;
    return (
      <StoryFrame>
        <div className="si-card w-(--checks-width) p-2">
          <div role="listbox" aria-label="Checks" tabIndex={-1}>
            <div role="group" aria-labelledby="story-response-codes">
              <ListHeading id="story-response-codes" level="none" meta="4 passed">
                Response codes
              </ListHeading>
              {rows.map((row, index) => (
                <ListRow
                  key={row.id}
                  asChild
                  icon={<SeverityMark severity={row.severity} />}
                  title={row.title}
                  meta={<Num value={row.count} />}
                  className={index === 0 ? 'is-hover' : index === 1 || selected === row.id ? 'is-focus' : undefined}
                >
                  <div
                    role="option"
                    aria-selected={selected === row.id}
                    tabIndex={selected === row.id ? 0 : -1}
                    onClick={() => setSelected(row.id)}
                    onKeyDown={(event) => {
                      if (event.key === 'Enter') setSelected(row.id);
                    }}
                  />
                </ListRow>
              ))}
              <ListRow
                asChild
                muted
                icon={<SeverityMark severity="skipped" />}
                title="Orphan page in a sitemap"
                meta={<Badge variant="dashed">Skipped</Badge>}
                description="No sitemap could be read, so orphan pages weren’t checked."
              >
                <div role="option" aria-selected={false} tabIndex={-1} />
              </ListRow>
            </div>
          </div>
        </div>
      </StoryFrame>
    );
  },
};

/** Filter chips that read as sentences: editable and removable, a preview, small, and the add chip. */
export const FilterChips: Story = {
  parameters: { pseudo: { hover: ['.is-hover'], focusVisible: ['.is-focus .si-chip__main'] } },
  render: function FilterChips() {
    const [chips, setChips] = useState(['status', 'indexable', 'check']);
    return (
      <StoryFrame sheet>
        <StateRow label="A filter bar">
          <SearchField label="Search URLs" placeholder="Search URLs" className="w-(--search-width-filter)" />
          {chips.includes('status') ? (
            <FilterChip
              field="Status"
              value={<StatusCode code={404} />}
              valueText="404"
              onEdit={() => undefined}
              onRemove={() => setChips((list) => list.filter((chip) => chip !== 'status'))}
            />
          ) : null}
          {chips.includes('indexable') ? (
            <FilterChip
              field="Indexable"
              value="yes"
              onEdit={() => undefined}
              onRemove={() => setChips((list) => list.filter((chip) => chip !== 'indexable'))}
            />
          ) : null}
          {chips.includes('check') ? (
            <FilterChip
              field="Check"
              value="Internal links without anchor text"
              onEdit={() => undefined}
              onRemove={() => setChips((list) => list.filter((chip) => chip !== 'check'))}
            />
          ) : null}
          <AddFilterChip />
          {chips.length < 3 ? (
            <Button size="sm" variant="ghost" onClick={() => setChips(['status', 'indexable', 'check'])}>
              Reset
            </Button>
          ) : null}
        </StateRow>
        <StateRow label="Hover · focus">
          <FilterChip
            field="Depth"
            op="is at least"
            value="4"
            onEdit={() => undefined}
            onRemove={() => undefined}
            className="is-hover"
          />
          <span className="is-focus">
            <FilterChip field="Depth" op="is at least" value="4" onEdit={() => undefined} onRemove={() => undefined} />
          </span>
          <AddFilterChip className="is-hover" />
        </StateRow>
        <StateRow label="Static · preview · small">
          <FilterChip field="Linked URL" value="/" onRemove={() => undefined} />
          <FilterChip field="Status" value="4xx" variant="preview" />
          <FilterChip field="Check" value="Internal links without anchor text" size="sm" variant="preview" />
          <FilterChip field="Linked URL" value="/" size="sm" onRemove={() => undefined} />
        </StateRow>
        <StateRow label="Long value, truncated to its room">
          <span className="flex w-(--popover-width) min-w-0 gap-2 overflow-hidden">
            <FilterChip
              field="Check"
              value="Another version of the site answers with a page instead of redirecting to the main one"
              variant="preview"
              size="sm"
              truncate
            />
            <FilterChip field="Detail" value="Root-relative: starts with /" variant="preview" size="sm" truncate />
          </span>
        </StateRow>
      </StoryFrame>
    );
  },
};

/**
 * Headers: a page's large title with its answer, a section's title (long, in a narrow column, it wraps), and a grid
 * screen's one line with its count.
 */
export const Headers: Story = {
  render: () => (
    <StoryFrame>
      <PageHeader
        title="Issues"
        subtitle="19 checks found 933 issues on this crawl. 63 passed and 3 were skipped."
        actions={<Button icon={Download}>Export all checks</Button>}
      />
      <PageHeader
        title="Overview"
        badge={<LiveBadge />}
        subtitle="Crawling at the Polite speed: 2 requests at a time."
      />
      <SectionHeader title="Library" meta="7 sites, 11 crawls" />
      <SectionHeader title="Crawling now" size="sm" actions={<Button size="sm">Open</Button>} />
      {/* A long title in a narrow column: the actions go under it, and it wraps. */}
      <div className="w-(--checks-width)">
        <SectionHeader
          title="Pages whose canonical points at a redirected URL"
          meta="82,904"
          actions={<Button size="sm">Export</Button>}
        />
      </div>
      <GridHeader
        title="URLs"
        count={<ResultCount shown={264} total={4521} noun="URL" />}
        badge={<LiveBadge />}
        actions={<SearchField label="Search URLs and titles" placeholder="Search URLs and titles" shortcut="mod+f" />}
      />
      <GridHeader title="Links" count={<ResultCount shown={21_914} total={21_914} noun="link" />} />
      <Facts>
        <Fact label="App">2.0.0-alpha.1</Fact>
        <Fact label="Engine">2.0.0-alpha.1 (sidecar)</Fact>
        <Fact label="Data folder">
          <span className="font-mono text-12">C:\Users\lee\AppData\Local\ShoutingIguana</span>
        </Fact>
      </Facts>
    </StoryFrame>
  ),
};
