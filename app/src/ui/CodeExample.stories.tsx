import type { Meta, StoryObj } from '@storybook/react-vite';
import { OnSurfaces, StoryFrame, StorySection } from '../test/StoryFrame';
import { CodeExample } from './CodeExample';

const meta = { title: 'Design system/Code example' } satisfies Meta;
export default meta;
type Story = StoryObj<typeof meta>;

/**
 * A fix as code: the code as it is over the code as it should be. What's new is marked in the accent's tint with a
 * rule under it, what goes is struck through, and the labels are words.
 */
export const BeforeAndAfter: Story = {
  render: () => (
    <StoryFrame sheet>
      <StorySection title="An attribute added">
        <CodeExample
          language="html"
          before={'<a href="/"><svg viewBox="0 0 120 32">…</svg></a>'}
          after={'<a href="/" aria-label="Example Furniture"><svg viewBox="0 0 120 32">…</svg></a>'}
        />
      </StorySection>
      <StorySection title="A value changed">
        <CodeExample
          language="html"
          before={'<meta name="robots" content="noindex, follow">'}
          after={'<meta name="robots" content="index, follow">'}
        />
      </StorySection>
      <StorySection title="An element renamed, on one of two lines">
        <CodeExample
          language="html"
          before={'<h1>Oak dining tables</h1>\n<h1>Delivery and returns</h1>'}
          after={'<h1>Oak dining tables</h1>\n<h2>Delivery and returns</h2>'}
        />
      </StorySection>
      <StorySection title="A line taken out, and where the fix goes">
        <CodeExample
          language="robotsTxt"
          where="in robots.txt, at the rule the detail names"
          before={'User-agent: *\nDisallow: /blog/\nDisallow: /cart/'}
          after={'User-agent: *\nDisallow: /cart/'}
        />
      </StorySection>
    </StoryFrame>
  ),
};

/** Something that isn't there at all: one block, "Add", with where it goes beside the label. */
export const AddWithWhere: Story = {
  render: () => (
    <StoryFrame sheet>
      <StorySection title="A tag for the head">
        <CodeExample
          language="html"
          where="in the page’s <head>"
          after={
            '<meta name="description" content="Solid oak dining tables in six sizes, made to order and delivered free.">'
          }
        />
      </StorySection>
      <StorySection title="An entry in an XML sitemap">
        <CodeExample
          language="xml"
          where="in your XML sitemap, inside <urlset>"
          after={'<url>\n  <loc>https://www.example.com/oak-dining-tables/</loc>\n</url>'}
        />
      </StorySection>
      <StorySection title="A line of robots.txt">
        <CodeExample
          language="robotsTxt"
          where="anywhere in robots.txt"
          after="Sitemap: https://www.example.com/sitemap.xml"
        />
      </StorySection>
    </StoryFrame>
  ),
};

/** Response headers: each header's name is coloured, and the label says what kind of code it is. */
export const HttpHeader: Story = {
  render: () => (
    <StoryFrame sheet>
      <StorySection title="A header changed">
        <CodeExample
          language="httpHeader"
          before="Set-Cookie: sessionid=38afes7a8; Path=/"
          after="Set-Cookie: sessionid=38afes7a8; Path=/; Secure; HttpOnly; SameSite=Lax"
        />
      </StorySection>
      <StorySection title="Headers to add">
        <CodeExample
          language="httpHeader"
          where="in the server’s or CDN’s response headers, for every page"
          after={
            'Strict-Transport-Security: max-age=31536000\nX-Content-Type-Options: nosniff\n' +
            "Referrer-Policy: strict-origin-when-cross-origin\nContent-Security-Policy: frame-ancestors 'self'"
          }
        />
      </StorySection>
      <StorySection title="JSON, as a JSON-LD block has it">
        <CodeExample
          language="json"
          before={'{\n  …\n  "name": "Oak dining table",\n}'}
          after={'{\n  …\n  "name": "Oak dining table"\n}'}
        />
      </StorySection>
    </StoryFrame>
  ),
};

/**
 * Long lines wrap inside the example, which never scrolls sideways and never grows past about 100 characters: in a
 * wide sheet, and in a column 320 px wide.
 */
export const LongLinesWrap: Story = {
  render: () => {
    const example = (
      <CodeExample
        language="html"
        before={'<img src="/img/hero-1600.jpg" alt="Oak dining table">'}
        after={
          '<img src="/img/hero-1600.jpg" alt="Oak dining table"\n' +
          '     srcset="/img/hero-480.jpg 480w, /img/hero-800.jpg 800w, /img/hero-1600.jpg 1600w"\n' +
          '     sizes="(max-width: 800px) 100vw, 800px">'
        }
      />
    );
    return (
      <StoryFrame sheet>
        <StorySection title="In a wide sheet: no wider than 100 characters">{example}</StorySection>
        <StorySection title="In a column 320 px wide">
          <div className="w-(--simple-words-min)">{example}</div>
        </StorySection>
        <StorySection title="A long URL with nowhere to break">
          <div className="w-(--simple-words-min)">
            <CodeExample
              language="html"
              where="in the page’s <head>, with the page’s own full URL"
              after={
                '<link rel="canonical" href="https://www.example.com/dining-room/tables/oak-dining-tables-extending/">'
              }
            />
          </div>
        </StorySection>
      </StoryFrame>
    );
  },
};

/** On each surface it can sit on: the frame and the two blocks stay apart from what's behind them. */
export const OnEachSurface: Story = {
  render: () => (
    <StoryFrame>
      <OnSurfaces>
        <CodeExample language="html" before="<html>" after={'<html lang="en-GB">'} />
      </OnSurfaces>
    </StoryFrame>
  ),
};
