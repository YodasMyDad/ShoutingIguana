using ShoutingIguana.Engine.Analysis.Models;

namespace ShoutingIguana.Engine.Analysis;

/// <summary>
/// The checks in plain words, by check id (docs/v2-ui-simple.md, "Plain words"). They live here, apart from
/// <see cref="IssueCatalog"/> and the rules, so every check's words can be written and reviewed in one place. They are
/// words, not results: no part of <see cref="AnalysisFreshness.ChecksFingerprint"/>, so writing or changing one makes no
/// analysis out of date.
/// <para>
/// A check with no entry has no plain words yet: hosts get null for it, never an empty text or the technical one in its
/// place. Today these are the ten checks the design document writes out, word for word. To add a check, add its entry: a
/// test holds every entry to the template's limits and the words the design rules out.
/// </para>
/// <para>
/// A text names a threshold as <c>{limit:name}</c>, never as a number: <c>{limit:titleMaxLength}</c> reads "60 characters"
/// once <see cref="PlainLimits.Fill(IssuePlainText, AnalysisSettings)"/> has filled it from the settings an analysis ran
/// with, so the words follow a changed threshold. The entries here are as written, tokens and all: hosts show them filled.
/// </para>
/// </summary>
public static class IssuePlainTexts
{
    private static readonly Dictionary<string, IssuePlainText> BY_CHECK = new(StringComparer.Ordinal)
    {
        ["robots-txt-unreachable"] = new(
            """Search engines can't read your site's rules file""",
            [
                """The file that tells search engines where they may go on your site (its "robots.txt") answers with an error.""",
                """Google stops visiting the whole site when that happens, so new and changed pages don't reach search results.""",
                """This is one fault for the whole site, and the first thing to fix."""
            ],
            [
                """Open your site's address followed by /robots.txt in a browser, to see whether it loads for you.""",
                """Send this to your developer or hosting company today: the file must answer normally, or with "not found" if you don't need one.""",
                """Ask them to make sure the firewall or security service isn't turning away requests for it.""",
                """Check the site again once it's fixed."""
            ],
            IssueFixer.Developer,
            """The rules file for search engines isn't returning an error"""),

        ["internal-client-error"] = new(
            """Pages or files that can't be found""",
            [
                """Some addresses on your site answer with an error instead of a page or file, most often "not found" (a 404).""",
                """Visitors who follow a link to one get an error page, and Google drops addresses like these from its results.""",
                """That's the right answer for something you removed on purpose, as long as nothing still links to it."""
            ],
            [
                """Look at each address in the list and decide whether it should still exist.""",
                """If it should, put the page or file back.""",
                """If it has moved, forward the old address to the new one (a "301 redirect"), in your site's redirect settings or through your developer.""",
                """If it's gone for good, leave it, and remove the links that still point to it."""
            ],
            IssueFixer.Either,
            """Every page and file on the site can be found"""),

        ["broken-internal-links"] = new(
            """Links that lead to broken pages""",
            [
                """Some of your pages have links that lead to an address on your site that doesn't work.""",
                """Visitors who click one land on an error page, and search engines waste their visit on it.""",
                """A link in a menu or footer repeats on every page, so one bad link can show up hundreds of times."""
            ],
            [
                """Open each page in the list in your site's editor and find the link.""",
                """Point it at the right page, or remove it.""",
                """If the same link is on most pages, it's in your menu, footer or page design: fix it there once."""
            ],
            IssueFixer.You,
            """Every link on the site leads to a working page"""),

        ["empty-anchor-text"] = new(
            """Links with nothing to read""",
            [
                """Some links on your pages have no words: usually an image or icon, such as a logo, with no description.""",
                """People who use a screen reader hear only "link", and search engines learn nothing about the page it leads to.""",
                """A logo or icon in the page design is the usual cause, so one fix often clears every page."""
            ],
            [
                """Find the link on one of the pages: the list shows where it leads.""",
                """If it's an image, give the image a short description (its "alt text"), such as your site's name for a logo.""",
                """If it's an icon, ask your developer to add a label to the link (an "aria-label")."""
            ],
            IssueFixer.Either,
            """Every link has words to read""")
        {
            OneFix = """That's almost certainly your logo, which sits in the part of the site every page shares."""
        },

        ["canonicalised-page"] = new(
            """Pages that name another page as the main version""",
            [
                """These pages tell search engines that another page is the main version of them (a "canonical" link).""",
                """Google then shows that other page instead, which is right for copies, such as page 2 of a list or a print version.""",
                """It's a problem only if the page has its own content and should appear in search itself."""
            ],
            [
                """Look at each page and the page it points to, and decide whether they're really the same thing.""",
                """If they are, there's nothing to do.""",
                """If this page should appear in search on its own, ask your developer to point its canonical link at its own address."""
            ],
            IssueFixer.Either,
            """No page names another page as the main version"""),

        ["duplicate-site-version"] = new(
            """Your site answers at more than one address""",
            [
                """Your site shows its pages at more than one address, such as with and without "www", or on "http" as well as "https".""",
                """The spare addresses should forward to the main one.""",
                """As it is, search engines see two copies of everything and pick one themselves.""",
                """Visitors on an "http" copy also browse without encryption."""
            ],
            [
                """Decide which address is the main one: usually the "https" one your home page uses.""",
                """Ask your developer or hosting company to forward every other version to it (a "301 redirect"), page for page.""",
                """Check the site again to confirm."""
            ],
            IssueFixer.Developer,
            """The site answers at one address only"""),

        ["image-too-large"] = new(
            """Large images that slow pages down""",
            [
                """Some pages show images whose files are over 200 KB.""",
                """Big images are the most common reason a page feels slow, most of all on a phone, and people leave slow pages.""",
                """Photos uploaded straight from a camera or phone are the usual cause."""
            ],
            [
                """Resize each image so it's no bigger than it's shown on the page.""",
                """Save it as a compressed JPEG or WebP, then upload it in place of the old one.""",
                """If your site has an image-optimising setting or plugin, turn it on so new uploads are shrunk for you."""
            ],
            IssueFixer.You,
            """No image is too big to load quickly"""),

        ["title-duplicate"] = new(
            """Pages that share the same title""",
            [
                """Two or more pages have the same title: the text on the browser tab, and the link people click in search results.""",
                """Searchers can't tell the pages apart, and Google may make up a title of its own.""",
                """It often means two addresses show the same page, or the site's design leaves out what's different, such as the page number."""
            ],
            [
                """Open each page in your site's editor and give it a title that says what's specific to it.""",
                """If two addresses show the same page, keep one and ask your developer to forward the other to it."""
            ],
            IssueFixer.You,
            """Every page has its own title"""),

        ["slow-response"] = new(
            """Pages that are slow to answer""",
            [
                """These pages took more than 1 second to arrive when the check asked for them.""",
                """Nothing can appear on screen until the page arrives, so visitors wait, and Google visits slow sites less often.""",
                """One slow reading can be a blip, so try the pages yourself before acting."""
            ],
            [
                """Open two or three of the pages in your browser and see whether they're slow for you too.""",
                """If they are, send the list to your developer or hosting company and ask what the server is waiting for.""",
                """Ask about page caching and a faster hosting plan: they're the usual fixes."""
            ],
            IssueFixer.Developer,
            """No page is slow to answer"""),

        ["description-missing"] = new(
            """Pages with no search description""",
            [
                """These pages have no search description: the short text under a page's title in search results.""",
                """Without one (a "meta description"), Google picks some text from the page, which may not be what you'd choose.""",
                """It doesn't change where the page ranks, so start with the pages you most want people to click."""
            ],
            [
                """Open the page in your site's editor and find the description box, often under "SEO" or "Search engine listing".""",
                """Write one or two sentences that say what the page offers.""",
                """Skip pages nobody searches for, such as login and basket pages."""
            ],
            IssueFixer.You,
            """Every page has a search description""")
    };

    /// <summary>Every check that has plain words, by its id.</summary>
    public static IReadOnlyDictionary<string, IssuePlainText> All => BY_CHECK;

    /// <summary>The check's plain words, or null when it has none yet.</summary>
    public static IssuePlainText? Find(string checkId) => BY_CHECK.GetValueOrDefault(checkId);
}
