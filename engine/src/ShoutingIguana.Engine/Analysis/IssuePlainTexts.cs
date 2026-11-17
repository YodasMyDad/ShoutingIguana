// Written by engine/tests/ShoutingIguana.Engine.Tests/Analysis/PlainWords/generate.mjs from plain-words.json beside it.
// Don't edit this file: change the words in the data file and run the generator again. A test holds the two together.
using ShoutingIguana.Engine.Analysis.Models;

namespace ShoutingIguana.Engine.Analysis;

/// <summary>
/// The checks in plain words, by check id (docs/v2-ui-simple.md, "Plain words"). They live here, apart from
/// <see cref="IssueCatalog"/> and the rules, so every check's words can be written and reviewed in one place. They are
/// words, not results: no part of <see cref="AnalysisFreshness.ChecksFingerprint"/>, so writing or changing one makes no
/// analysis out of date.
/// <para>
/// Every check of the catalog has its words: a test holds that, and holds every entry to the template's limits and the
/// words the design rules out. A check without an entry (one a newer catalog adds before its words are written) has no
/// plain words: hosts get null for it, never an empty text or the technical one in its place.
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
            """No page or file answers with a "not found" error"""),

        ["internal-server-error"] = new(
            """Pages or files that fail with a server error""",
            [
                """Some addresses on your site answer with a server error, such as "internal server error" (a 500).""",
                """Visitors see an error page, and Google visits a site less often while the errors go on.""",
                """One error on a busy day can be a blip, but errors that repeat mean something on the server is broken."""
            ],
            [
                """Open two or three of the addresses in your browser to see whether they still fail.""",
                """If they do, send the list to your developer or hosting company: the cause will be in the server's error log.""",
                """If they load now, check the site again at the "Polite" speed: the check itself may have been too much for the server."""
            ],
            IssueFixer.Developer,
            """No page or file fails with a server error"""),

        ["internal-unreachable"] = new(
            """Pages or files that don't answer""",
            [
                """Some addresses on your site gave no answer at all: the connection failed, or the wait ran out.""",
                """Visitors get a browser error or a page that never loads, and Google can't list an address it gets nothing from.""",
                """It can also happen when the check itself asks faster than the server can cope with."""
            ],
            [
                """Open two or three of the addresses in your browser to see whether they load for you.""",
                """If they don't, send the list to your developer or hosting company and ask what is dropping or delaying connections.""",
                """If they do, check the site again at the "Polite" speed to see whether the check was too much for the server."""
            ],
            IssueFixer.Developer,
            """No page or file failed to answer"""),

        ["internal-blocked"] = new(
            """Pages or files your site's security wouldn't show""",
            [
                """Your site's firewall or security service turned the check away from some addresses, so they couldn't be read.""",
                """Any problems on those addresses are missing from these results.""",
                """Visitors rarely notice, but if search engines are turned away too, they can't list those addresses."""
            ],
            [
                """Open one of the addresses in your browser to see that it loads for you.""",
                """Ask your hosting company, or whoever runs the firewall, to let Shouting Iguana through, then check the site again.""",
                """Ask them to make sure Google and other search engines aren't turned away by the same rules.""",
                """Check again at the "Polite" speed if the list shows "too many requests" (a 429): the check was asking too fast."""
            ],
            IssueFixer.Developer,
            """No page or file was refused by the site's security"""),

        ["soft-404"] = new(
            """Pages that read like an error page""",
            [
                """These pages have a title or main heading that reads like an error message, such as "Page not found", and little text.""",
                """Yet they tell search engines they loaded normally, so Google keeps coming back to pages it won't show (it calls them "soft 404s").""",
                """If a page is real and its heading only mentions "not found", there's nothing to do."""
            ],
            [
                """Open each page in the list and see whether it's a real page or an error message.""",
                """If it's a real page, change the title or heading so it doesn't read like an error.""",
                """If it's an error message, ask your developer to make the address answer "not found" (a 404), or forward it to a replacement."""
            ],
            IssueFixer.Either,
            """No page reads like an error page"""),

        ["redirect-chain"] = new(
            """Addresses that forward more than once""",
            [
                """Some addresses on your site forward visitors on two or more times before they reach the final address.""",
                """Each extra step is a wait before the page starts to load, and Google gives up after 10 steps.""",
                """Two steps do little harm, but the steps pile up each time the site changes."""
            ],
            [
                """Change the links on your pages, and the addresses in your sitemap, to the final address: the list shows it.""",
                """Ask your developer or hosting company to forward each old address straight to the final one, in one step (a "301 redirect")."""
            ],
            IssueFixer.Either,
            """No address forwards more than once"""),

        ["redirect-loop"] = new(
            """Addresses that forward in a circle""",
            [
                """Some addresses on your site forward visitors round in a circle, so they never reach a page.""",
                """The browser gives up and shows an error such as "too many redirects", and search engines have nothing to list.""",
                """The usual cause is two forwarding rules that undo each other, such as one adding a slash to an address and one removing it."""
            ],
            [
                """Open one of the addresses in your browser to see the error for yourself.""",
                """Send the list to your developer or hosting company and ask them to find the two forwarding rules that undo each other.""",
                """Ask them to make each address end at a page that loads, then check the site again."""
            ],
            IssueFixer.Developer,
            """No address forwards in a circle"""),

        ["redirect-broken"] = new(
            """Addresses that forward to a dead end""",
            [
                """Some addresses on your site forward visitors on, but to an address that doesn't work, or to nowhere at all.""",
                """Visitors land on an error instead of the page they were promised, and search engines find nothing to list.""",
                """Every link to the old address, on your site and on others, leads to that error."""
            ],
            [
                """Look at where each address in the list ends up, and decide which working page should replace it.""",
                """Change the forwarding (the "redirect") to point at that page, in your site's redirect settings or through your developer.""",
                """If nothing replaces it, remove the forwarding so the old address answers "not found" (a 404), then remove the links to it."""
            ],
            IssueFixer.Either,
            """No address forwards to a dead end"""),

        ["redirect-temporary"] = new(
            """Forwarding marked as temporary""",
            [
                """Some addresses forward visitors on with a signal that the move is only temporary.""",
                """Google may then keep the old address in its results, which delays the switch when the move is really for good.""",
                """That's right for a short-term move, such as a sale page or a login step, and then there's nothing to do."""
            ],
            [
                """Look at each address in the list and decide whether its move is for good.""",
                """If it's temporary, leave it.""",
                """If it's for good, ask your developer or hosting company to change it from temporary (a "302 redirect") to permanent (a "301 redirect")."""
            ],
            IssueFixer.Developer,
            """No forwarding is marked as temporary"""),

        ["redirect-downgrade"] = new(
            """Secure addresses that forward to an insecure one""",
            [
                """Some secure addresses on your site (starting "https") forward visitors to an insecure one (starting "http").""",
                """Browsers label the page they land on "Not secure", and Google is then likely to list the insecure address.""",
                """The usual cause is an old forwarding rule that was never updated."""
            ],
            [
                """Open one of the addresses in your browser and look for "Not secure" beside the address it ends on.""",
                """Send the list to your developer or hosting company and ask them to forward each one to the "https" version of where it goes.""",
                """Check the site again once it's changed."""
            ],
            IssueFixer.Developer,
            """No secure address forwards to an insecure one"""),

        ["meta-refresh"] = new(
            """Pages that move on or reload by themselves""",
            [
                """These pages send visitors to another address, or reload, by themselves: at once, or after a set time.""",
                """A timed move takes the page away while people are still reading.""",
                """It's also slower than forwarding done by the server, which is what Google recommends.""",
                """It's an acceptable fallback only where the server can't forward."""
            ],
            [
                """Open one of the pages and watch what happens: the list shows where each one sends visitors, and when.""",
                """Ask your developer to replace it (it's called a "meta refresh") with forwarding done by the server (a "301 redirect").""",
                """If a page reloads to show fresh content, ask for it to update in place instead, or leave visitors to reload."""
            ],
            IssueFixer.Developer,
            """No page moves on or reloads by itself""")
        {
            OneFix = """That's almost certainly one line in your page design, which every page shares."""
        },

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
            """Every link between your pages leads to a working page"""),

        ["broken-external-links"] = new(
            """Links to other websites that don't work""",
            [
                """Some of your pages link to an address on another website that doesn't work.""",
                """Visitors who click one reach a dead end, which makes your page look out of date.""",
                """Some websites turn away automatic checks but work for people, so try a link yourself before you change it."""
            ],
            [
                """Click a few of the links in your browser to confirm they're really broken.""",
                """Open each page in your site's editor and point the link at the new address, if the page has moved.""",
                """Remove the link, or swap it for another source, if the page has gone for good."""
            ],
            IssueFixer.You,
            """No link to another website was found broken""")
        {
            OneFix = """That's almost certainly a link in your menu, footer or page design, which every page shares."""
        },

        ["links-to-redirects"] = new(
            """Links that go through a forwarding address""",
            [
                """Some links on your pages point at an address on your site that forwards visitors on to another one.""",
                """The link still works, but each click takes an extra step, and so does each visit from a search engine.""",
                """A few of these do no harm, so there's nothing to do unless the link is in a menu or footer that repeats on every page."""
            ],
            [
                """Open a page from the list in your site's editor and find the link: the list shows the address it points at.""",
                """Change the link to the address it ends up at.""",
                """Leave the forwarding itself in place, for bookmarks and for links from other websites."""
            ],
            IssueFixer.You,
            """No link between your pages goes through a forwarding address""")
        {
            OneFix = """That's almost certainly a link in your menu, footer or page design, which every page shares."""
        },

        ["internal-nofollow-links"] = new(
            """Links that search engines are told not to follow""",
            [
                """Some links between your own pages carry a note that tells search engines not to follow them.""",
                """Pages reached only through links like these (marked "nofollow") may be found late by search engines, or not at all.""",
                """It's common on login and basket links, where it does little harm and little good."""
            ],
            [
                """Open a page from the list in your site's editor and find the link: the list shows where it leads.""",
                """Remove the "nofollow" setting from the link, often a tick box in the link's options.""",
                """If the link is in your menu, footer or page design, ask your developer to take the setting off there, once.""",
                """If the aim was to keep a page out of search results, ask your developer to set that on the page itself (a "noindex" setting)."""
            ],
            IssueFixer.Either,
            """No link between your pages is marked not to be followed""")
        {
            OneFix = """That's almost certainly a link in your menu, footer or page design, which every page shares."""
        },

        ["empty-anchor-text"] = new(
            """Links with nothing to read""",
            [
                """Some links on your pages have no words: usually an image or icon, such as a logo, with no description.""",
                """People who use a screen reader hear only "link", and search engines learn nothing about the page it leads to.""",
                """A logo or icon in the page design is the usual cause, so one fix often clears every page.""",
                """Pages are still found either way, so it can wait."""
            ],
            [
                """Find the link on one of the pages: the list shows where it leads.""",
                """If it's an image, give the image a short description (its "alt text"), such as your site's name for a logo.""",
                """If it's an icon, ask your developer to add a label to the link (an "aria-label")."""
            ],
            IssueFixer.Either,
            """Every link between your pages has words to read""")
        {
            OneFix = """That's almost certainly your logo, which sits in the part of the site every page shares."""
        },

        ["broken-fragment-links"] = new(
            """Links to a part of a page that isn't there""",
            [
                """Some links point to a part of a page, such as a heading further down, but that part can't be found.""",
                """Visitors land at the top of the page instead of the section the link promised.""",
                """Search engines still follow the link to the page, so only visitors are affected.""",
                """Some pages build their sections after loading, so if a link works in your browser there's nothing to do."""
            ],
            [
                """Click the link on one of the pages and see whether it jumps to the right section.""",
                """If it doesn't, compare the part of the link after the "#" with the name the section was given (its "anchor" or "id").""",
                """Make the two match exactly in your site's editor, or ask your developer to put the name back on the section."""
            ],
            IssueFixer.Either,
            """No link points to a missing part of a page""")
        {
            OneFix = """That's almost certainly a link in your page design, such as "Skip to content", which every page shares."""
        },

        ["orphan-page"] = new(
            """Pages in your sitemap that nothing links to""",
            [
                """No page on your site links to these pages: they were found only in your sitemap.""",
                """Visitors can't reach them by browsing, and Google relies on links to find pages and judge what they're about.""",
                """They're often old pages left in the sitemap (the file that lists your pages for search engines), or pages reached only through a form."""
            ],
            [
                """Decide, for each page, whether you still want it.""",
                """If you do, link to it from a page where it belongs, such as its category.""",
                """If you don't, remove it from the site and from the sitemap."""
            ],
            IssueFixer.You,
            """No page in the sitemap is left with no links to it"""),

        ["noindex-page"] = new(
            """Pages told to stay out of search results""",
            [
                """These pages carry an instruction that tells search engines to keep them out of search results.""",
                """Google drops such a page from its results completely (the instruction is called "noindex").""",
                """That's right for thank-you, account and site-search pages, and then there's nothing to do.""",
                """It's a problem only on a page you want people to find, where it's often left over from when the site was built."""
            ],
            [
                """Look down the list for any page you want people to find in search.""",
                """Open that page in your site's editor and find a setting such as "Hide this page from search engines", often under "SEO".""",
                """Turn the setting off, or ask your developer to remove the "noindex" from the page."""
            ],
            IssueFixer.Either,
            """No page is told to stay out of search results"""),

        ["nofollow-page"] = new(
            """Pages that tell search engines not to follow their links""",
            [
                """These pages tell search engines not to follow any of the links on them.""",
                """Pages that are linked only from here may never be found by search engines.""",
                """That's rarely meant: it's usually left over from when the site was built, or set by a plugin."""
            ],
            [
                """Open one of the pages in your site's editor and find a setting about following links, often under "SEO" or "Advanced".""",
                """Set it to follow links, or ask your developer to remove the "nofollow" setting from the page.""",
                """If only one or two links shouldn't be followed, ask your developer to mark those links instead of the whole page."""
            ],
            IssueFixer.Either,
            """No page tells search engines not to follow its links"""),

        ["blocked-by-robots"] = new(
            """Addresses that search engines are told not to visit""",
            [
                """Your site's rules file for search engines puts these addresses off limits, so the check didn't read them.""",
                """Search engines can't see what's on them, though Google may still list the bare address if other pages link to it.""",
                """That's right for baskets and site searches, and then there's nothing to do.""",
                """It's a problem if the rule catches pages you want found."""
            ],
            [
                """Look down the list for any page you want people to find, and for files ending in .css or .js, which pages need to display.""",
                """If there are none, leave the rules as they are.""",
                """If there are, ask your developer to change the rule that covers them in the rules file (its "robots.txt").""",
                """If the list says the rules file couldn't be read, fix that first: it's listed as a problem of its own."""
            ],
            IssueFixer.Developer,
            """No address is off limits to search engines"""),

        ["snippet-blocked"] = new(
            """Pages set to show no text in search results""",
            [
                """These pages tell search engines to show no text from the page under its title in search results.""",
                """The result is a bare title and address, which tells searchers less, and Google won't quote the page in its AI answers.""",
                """That can be deliberate, for private or paid-for content, and then there's nothing to do."""
            ],
            [
                """Search Google for one of the pages and see whether its result shows any text under the title.""",
                """Decide whether you meant these pages to appear that way.""",
                """If not, ask your developer to remove the setting the list names, such as "nosnippet", from the pages."""
            ],
            IssueFixer.Developer,
            """No page blocks its text from search results""")
        {
            OneFix = """These pages most likely get it from one setting, in your page design or SEO plugin."""
        },

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
            """The rules file for search engines isn't returning an error""")
        {
            LooksFor = """A rules file that search engines can't read"""
        },

        ["robots-txt-unreachable-other-origin"] = new(
            """The rules file on another part of your site can't be read""",
            [
                """The rules file for search engines answers with an error on another part of your site, such as shop.example.com.""",
                """Google stops visiting that part of the site when that happens, while your main site carries on as normal.""",
                """It matters only if that part holds pages or files you want people to find."""
            ],
            [
                """Open the address the list names in your browser, to see whether the file loads for you.""",
                """Send it to your developer or hosting company: the file (a "robots.txt") must answer normally, or with "not found" if it isn't needed.""",
                """If that part of the site shouldn't exist, remove the links to it."""
            ],
            IssueFixer.Developer,
            """No other part of the site has a rules file that fails""")
        {
            LooksFor = """A rules file that fails on another part of your site"""
        },

        ["robots-txt-missing"] = new(
            """A rules file for search engines is missing""",
            [
                """Search engines that ask for your site's rules file (a "robots.txt") get "not found", or are refused.""",
                """They take that to mean they may visit everything, so nothing is held back.""",
                """What you miss is a place to tell them where your sitemap is, and to keep them out of pages such as baskets.""",
                """If you need neither, there's nothing to do."""
            ],
            [
                """Open the address the list names in a browser, to see what answers.""",
                """Look in your site's SEO settings or plugin for a "robots.txt" option: most can create the file.""",
                """Ask your developer to add one that names your sitemap, if your site has no such option.""",
                """Ask your hosting company to let requests for the file through, if a firewall is refusing them."""
            ],
            IssueFixer.Either,
            """The site isn't missing its rules file for search engines""")
        {
            LooksFor = """A missing rules file for search engines"""
        },

        ["site-hidden-from-search"] = new(
            """Your site hides most of its pages from search""",
            [
                """Your home page and more than half of your pages tell search engines to keep them out of search results.""",
                """Those pages can't be found through Google, however good they are.""",
                """It's often one setting left on from when the site was being built, so one change may fix it.""",
                """This is one fault for the whole site, and the first thing to fix."""
            ],
            [
                """Look in your site's settings for an option such as "Discourage search engines" or "Hide site from search", and turn it off.""",
                """If there's no such option, send this to your developer: the list says how many pages are hidden, and how.""",
                """Ask them to remove the "noindex" setting.""",
                """Check the site again once it's changed."""
            ],
            IssueFixer.Either,
            """The site as a whole isn't hidden from search""")
        {
            LooksFor = """A site that hides its home page and most pages from search"""
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

        ["canonical-missing"] = new(
            """Pages that don't name their main address""",
            [
                """These pages don't say which address is their main one (they have no "canonical" link).""",
                """The same page can often be reached at several addresses, and without this Google picks one itself.""",
                """Google usually picks well, so there's nothing to do unless each page can be reached at many addresses."""
            ],
            [
                """See whether your site's SEO settings or plugin can add a canonical link to every page: most can.""",
                """If not, ask your developer to add one to the page design, pointing each page at its own address."""
            ],
            IssueFixer.Either,
            """Every page names its main address"""),

        ["canonical-multiple"] = new(
            """Pages that name more than one main address""",
            [
                """These pages name two or more different addresses as their main one (they have conflicting "canonical" links).""",
                """Google is likely to ignore them all and choose the main address itself.""",
                """It usually means two parts of your site each add one, such as the page design and an SEO plugin."""
            ],
            [
                """Check whether your site has two SEO plugins, or a page design with SEO settings of its own, both switched on.""",
                """Turn one of them off, so each page names a single main address.""",
                """If you can't find a second source, ask your developer what else adds a canonical link: it can also come from the server."""
            ],
            IssueFixer.Either,
            """No page names more than one main address"""),

        ["canonical-broken"] = new(
            """Pages that name a broken address as their main one""",
            [
                """These pages name a main address that doesn't work (their "canonical" link leads to an error).""",
                """Search engines can't show an address that fails, so the instruction does nothing and they choose for themselves.""",
                """The cause is usually an out-of-date address, such as one from a test copy of the site."""
            ],
            [
                """Open the main address the list shows for one of the pages in your browser, to see the error.""",
                """If your site's editor has a "canonical" box for the page, often under "SEO", put the page's own address in it.""",
                """If the same wrong address is on many pages, ask your developer to correct it in the page design."""
            ],
            IssueFixer.Either,
            """No page names a broken address as its main one""")
        {
            OneFix = """That's almost certainly one setting in your page design or SEO plugin, which names the same address on every page."""
        },

        ["canonical-to-redirect"] = new(
            """Pages that name a forwarding address as their main one""",
            [
                """These pages name a main address (a "canonical" link) that forwards visitors on to another address.""",
                """That gives search engines two answers to one question, and they may not pick the one you want.""",
                """Often it forwards straight back to the page itself, which does little harm but is still worth tidying."""
            ],
            [
                """Look at where each main address in the list ends up: the list names the address, and says when it couldn't be checked.""",
                """Put that final address in the "canonical" box of your site's editor, often under "SEO", if it has one.""",
                """Ask your developer to correct it in the page design if the same thing shows on many pages."""
            ],
            IssueFixer.Either,
            """No page names a forwarding address as its main one""")
        {
            OneFix = """That's almost certainly one setting in your page design or SEO plugin, which names the same address on every page."""
        },

        ["canonical-to-non-indexable"] = new(
            """Pages that name a main address search engines can't show""",
            [
                """These pages name a main address (a "canonical" link) that can't itself appear in search results.""",
                """The page says "show that one instead", while that one says "don't show me", so the outcome is left to Google.""",
                """The list says what holds each one back, such as an instruction to stay out of search."""
            ],
            [
                """Look at the main address the list shows for each page, and why it can't appear in search.""",
                """If that address should appear in search, ask your developer to remove what holds it back.""",
                """If it shouldn't, ask them to point the page's canonical link at its own address, or at one that can appear."""
            ],
            IssueFixer.Developer,
            """No page names a main address that search can't show""")
        {
            OneFix = """That's almost certainly one setting in your page design or SEO plugin, which names the same address on every page."""
        },

        ["canonical-outside-head"] = new(
            """Main address written in the wrong part of the page""",
            [
                """These pages name their main address (a "canonical" link) in a part of the page where search engines ignore it.""",
                """So the pages have no main address in effect, and Google chooses one itself.""",
                """It usually means something added near the top of the page's code, by a plugin or script, has pushed it out of place."""
            ],
            [
                """Note any plugin, tracking code or banner added to the site lately, as one of them may be the cause.""",
                """Send the list to your developer: the canonical link must sit in the page's "head" section.""",
                """Ask them to move or remove whatever ends that section early."""
            ],
            IssueFixer.Developer,
            """No main address is written in the wrong part of the page""")
        {
            LooksFor = """Main addresses written in the wrong part of the page"""
        },

        ["canonical-relative"] = new(
            """Main address written as a partial address""",
            [
                """These pages give their main address in a short form, such as /page, without "https://" and your site's name.""",
                """Google can read the short form, but recommends the full address.""",
                """A short form can end up pointing at the wrong place, for example on a test copy of the site.""",
                """If your main addresses already lead to the right pages, there's nothing to do now."""
            ],
            [
                """Send the list to your developer when they next work on the site: it isn't urgent.""",
                """Ask them to write each page's main address (its "canonical" link) in full, starting with "https://" and your site's name."""
            ],
            IssueFixer.Developer,
            """No main address is written as a partial address""")
        {
            OneFix = """That's almost certainly how your page design or SEO plugin writes the main address on every page.""",
            LooksFor = """Main addresses written as partial addresses"""
        },

        ["canonical-invalid"] = new(
            """Pages whose main address isn't a real web address""",
            [
                """These pages name a main address (a "canonical" link) that is empty or isn't a real web address.""",
                """Search engines ignore it, which can leave the page with no main address at all.""",
                """It's usually a gap in the page design that was never filled in."""
            ],
            [
                """Look at what the list shows for each page: it says whether the address is empty or badly formed.""",
                """Send the list to your developer and ask them to make each page name its own full address, or remove the broken line."""
            ],
            IssueFixer.Developer,
            """No page names a main address that isn't a real address""")
        {
            OneFix = """That's almost certainly a gap in your page design, which writes the same mistake on every page."""
        },

        ["noindex-with-canonical"] = new(
            """Pages that give search engines two opposite instructions""",
            [
                """These pages tell search engines to keep them out of search, and also name another page as their main version.""",
                """One instruction says "drop this page", the other says "treat it as a copy of that one", and Google doesn't say which wins.""",
                """Each page needs one or the other, not both."""
            ],
            [
                """Decide, for each page, whether it's a copy of the page it names or a page that should simply stay out of search.""",
                """If it's a copy, ask your developer to remove the instruction to stay out of search (the "noindex").""",
                """If it should stay out of search, ask them to point its main address (its "canonical" link) at the page itself."""
            ],
            IssueFixer.Developer,
            """No page gives search engines opposite instructions""")
        {
            OneFix = """That's almost certainly one setting in your page design or SEO plugin, which names the same address on every page."""
        },

        ["sitemap-non-indexable"] = new(
            """Sitemap addresses that can't appear in search""",
            [
                """Your sitemap, the file that lists your pages for search engines, includes addresses that can't appear in search.""",
                """Each one forwards elsewhere, fails, or is kept out of search some other way: the list says which.""",
                """A sitemap should hold only the addresses you want found, and these waste search engines' visits."""
            ],
            [
                """Look at the reason the list gives for each address.""",
                """If the address should be found, fix that reason: it's often listed as a problem of its own.""",
                """If it shouldn't, take it out of the sitemap, or list the address it forwards to instead.""",
                """Ask your developer to change how the sitemap is built if your site makes it for you and has no setting for this."""
            ],
            IssueFixer.Either,
            """No sitemap lists an address that can't appear in search"""),

        ["not-in-sitemap"] = new(
            """Pages missing from your sitemap""",
            [
                """These pages can appear in search, but aren't in your sitemap: the file that lists your pages for search engines.""",
                """Search engines can still find them through links, which is enough on a small site.""",
                """On a big or fast-changing site, a full sitemap helps new pages get found sooner.""",
                """Leaving out some kinds of page on purpose is fine, and then there's nothing to do."""
            ],
            [
                """Look down the list for pages you want found quickly, such as new products or articles.""",
                """Find the sitemap options in your site's SEO settings or plugin, and include the kinds of page that are missing.""",
                """Ask your developer to add them if your sitemap is built another way."""
            ],
            IssueFixer.Either,
            """No page is missing from the sitemap"""),

        ["sitemap-error"] = new(
            """A sitemap on your site can't be read""",
            [
                """A sitemap your site points search engines to can't be read: it's missing, fails to load, or has mistakes in it.""",
                """A sitemap is a file that lists your pages for search engines, and they get nothing from one they can't read.""",
                """Pages with links to them are still found, but new pages may take longer to show up in search."""
            ],
            [
                """Open the sitemap's address from the list in your browser, to see the error.""",
                """If your site's SEO settings or plugin make the sitemap, use them to build it again.""",
                """If the sitemap no longer exists, ask your developer to remove the line that names it from your rules file (its "robots.txt").""",
                """Send the error to your developer otherwise: the file must load and be a valid sitemap of at most 50,000 addresses."""
            ],
            IssueFixer.Either,
            """No sitemap fails to load or has mistakes in it""")
        {
            LooksFor = """A sitemap that can't be read"""
        },

        ["sitemap-missing"] = new(
            """No sitemap found""",
            [
                """The check found no sitemap: a file that lists your pages for search engines.""",
                """A small site whose pages all link to each other can manage without one, as Google finds pages by following links.""",
                """It helps most on big or new sites.""",
                """If you gave your sitemap straight to Google, in Search Console, it won't show here, and there's nothing to do."""
            ],
            [
                """Look in your site's SEO settings or plugin: most can create a sitemap with one switch.""",
                """Ask your developer to add its address to the site's rules file for search engines (its "robots.txt"), so they all find it.""",
                """Check the site again: two more checks can then run."""
            ],
            IssueFixer.Either,
            """A sitemap was found, or the site names one""")
        {
            LooksFor = """A missing sitemap"""
        },

        ["sitemap-url-other-host"] = new(
            """Sitemap addresses outside its own website""",
            [
                """Your sitemap lists addresses on another website, or on another version of your own, such as with or without "www".""",
                """Search engines drop those entries, so the pages they were meant for aren't put forward.""",
                """It's common after a site moves to or from "www"."""
            ],
            [
                """Compare the start of the addresses in the list with the start of your sitemap's own address.""",
                """Build the sitemap again from your site's SEO settings or plugin, so it lists each page at its current address.""",
                """Ask your developer to correct the web address your site is set to, if the sitemap keeps using the old one.""",
                """Take out any address that belongs to another website altogether."""
            ],
            IssueFixer.Either,
            """No sitemap lists an address outside its own website"""),

        ["title-missing"] = new(
            """Pages with no title""",
            [
                """These pages have no title: the text on the browser tab, and the link people click in search results.""",
                """Google has to make one up from other text on the page, which may not say what you'd choose.""",
                """Every page should have one, so fix these first."""
            ],
            [
                """Open the page in your site's editor and fill in its title, often under "SEO" or "Search engine listing".""",
                """Write a title that says what the page is about, and add your site's name if there's room.""",
                """If a whole kind of page has no title, ask your developer to set it in the page design."""
            ],
            IssueFixer.You,
            """Every page has a title"""),

        ["title-multiple"] = new(
            """Pages with more than one title""",
            [
                """These pages have more than one title in their code, where only one is allowed.""",
                """Browsers use the first, but search engines may use another, so the title you wrote may not be the one shown.""",
                """It usually means two parts of your site each write one, such as the page design and an SEO plugin."""
            ],
            [
                """Check whether your site has two SEO plugins, or a page design with SEO settings of its own, both switched on.""",
                """Turn one of them off, so each page gets a single title.""",
                """If you can't find a second source, ask your developer what else adds a title to the page."""
            ],
            IssueFixer.Either,
            """No page has more than one title"""),

        ["title-too-long"] = new(
            """Titles too long to show in full""",
            [
                """These pages have a title of more than {limit:titleMaxLength}.""",
                """Search results cut a long title off to fit the screen, so the last words may not show.""",
                """That's harmless when the important words come first, and then there's nothing to do."""
            ],
            [
                """Open the page in your site's editor and find its title.""",
                """Move the words that matter most to the front.""",
                """Cut repeated words and long endings, such as a slogan after your site's name."""
            ],
            IssueFixer.You,
            """No title is too long to show in full"""),

        ["title-too-short"] = new(
            """Very short titles""",
            [
                """These pages have a title of fewer than {limit:titleMinLength}.""",
                """Titles like "Login" or "Products" don't tell a searcher what the page is, or whose.""",
                """A short title can be exactly right, such as a product name, and then there's nothing to do."""
            ],
            [
                """Open the page in your site's editor and find its title.""",
                """Say what the page is about, and add your site's name if there's room.""",
                """Leave the titles that already say it all."""
            ],
            IssueFixer.You,
            """No title is very short"""),

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
            """No two pages share a title"""),

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
            """Every page has a search description"""),

        ["description-multiple"] = new(
            """Pages with more than one search description""",
            [
                """These pages have more than one search description in their code, where only one is allowed.""",
                """Search engines may read either one (each is a "meta description"), so the text you wrote may not be the one used.""",
                """It usually means two parts of your site each add one, such as the page design and an SEO plugin."""
            ],
            [
                """Check whether your site has two SEO plugins, or a page design with SEO settings of its own, both switched on.""",
                """Turn one of them off, so each page gets a single description.""",
                """If you can't find a second source, ask your developer what else adds a description to the page."""
            ],
            IssueFixer.Either,
            """No page has more than one search description"""),

        ["description-too-long"] = new(
            """Search descriptions too long to show in full""",
            [
                """These pages have a search description, the text under their title in search results, of more than {limit:descriptionMaxLength}.""",
                """Search results cut off the end of a long description (a "meta description").""",
                """That's harmless when the important words come first, so only pages where the cut loses the point need changing."""
            ],
            [
                """Open the page in your site's editor and find its description, often under "SEO".""",
                """Put what matters in the first sentence.""",
                """Trim the rest to about {limit:descriptionMaxLength}, or leave it if the opening already works."""
            ],
            IssueFixer.You,
            """No search description is too long to show in full"""),

        ["description-too-short"] = new(
            """Very short search descriptions""",
            [
                """These pages have a search description, the text under their title in search results, of fewer than {limit:descriptionMinLength}.""",
                """One that short (a "meta description") rarely says enough, so Google often shows text from the page instead.""",
                """A short one that says everything that matters is fine, and then there's nothing to do."""
            ],
            [
                """Open the page in your site's editor and find its description, often under "SEO".""",
                """Write one or two sentences that say what the page offers, and who it's for."""
            ],
            IssueFixer.You,
            """No search description is very short"""),

        ["description-duplicate"] = new(
            """Pages that share the same search description""",
            [
                """Two or more pages share one search description: the short text under a page's title in search results.""",
                """Searchers can't tell the pages apart, and Google tends to write its own text instead.""",
                """The shared text (a "meta description") often comes from a default that the site's design fills in."""
            ],
            [
                """Open each page in your site's editor and find its description, often under "SEO" or "Search engine listing".""",
                """Write one or two sentences that say what's on that page and nowhere else.""",
                """Leave numbered pages of one list (page 2, page 3) until last: they matter least."""
            ],
            IssueFixer.You,
            """No two pages share a search description""")
        {
            OneFix = """That's almost certainly a default description that your page design or SEO plugin gives to pages without one of their own."""
        },

        ["h1-missing"] = new(
            """Pages with no main heading""",
            [
                """These pages have no main heading (an "H1"): the headline that says what the page is about.""",
                """People using a screen reader rely on headings to find their way, and the page is harder to scan.""",
                """Often the headline is there to look at, but isn't marked as a heading.""",
                """It matters more to readers than to search, so it can wait until you next edit the page."""
            ],
            [
                """Open the page in your site's editor.""",
                """Select the headline at the top and set its style to "Heading 1".""",
                """If the headline is part of the page design, ask your developer to mark it as the H1."""
            ],
            IssueFixer.You,
            """Every page has a main heading""")
        {
            OneFix = """If these pages share one page design, marking its headline as the main heading fixes them all."""
        },

        ["h1-multiple"] = new(
            """Pages with more than one main heading""",
            [
                """These pages have more than one main heading (an "H1"), where one headline for the page is clearest.""",
                """Google says this doesn't matter for search, so it's about readers: screen readers use the main heading to say what the page is.""",
                """Often the page design marks the logo or a side panel's title as a main heading too.""",
                """The gain is small, so it can wait."""
            ],
            [
                """Open the page in your site's editor and see which lines are set to "Heading 1".""",
                """Keep that style for the page's headline, and set the others to "Heading 2" or plain text.""",
                """If the extra one is the logo or part of the page design, ask your developer to change it there."""
            ],
            IssueFixer.Either,
            """No page has more than one main heading"""),

        ["h1-duplicate"] = new(
            """Pages that share the same main heading""",
            [
                """Two or more pages have the same main heading (an "H1"): the headline that says what the page is about.""",
                """Visitors who land on one can't tell it from the other, and it often means the pages themselves are copies.""",
                """If the pages are clearly different in every other way, there's nothing to do."""
            ],
            [
                """Open each page in your site's editor and look at its headline.""",
                """Change it to say what's specific to that page, such as the product, place or topic.""",
                """If two addresses show the same page, keep one and ask your developer to forward the other to it."""
            ],
            IssueFixer.You,
            """No two pages share a main heading""")
        {
            OneFix = """That's almost certainly one heading in your page design, such as your site's name, marked as the main heading of every page."""
        },

        ["low-word-count"] = new(
            """Pages with very little text""",
            [
                """These pages have fewer than {limit:minWordCount} of their own, not counting menus and footers.""",
                """Short pages are often fine, and then there's nothing to do: a contact page doesn't need an essay.""",
                """This list is for spotting pages that are empty by mistake, such as one where the content failed to appear."""
            ],
            [
                """Open each page and see whether anything is missing.""",
                """If content is missing, put it back.""",
                """If the page is meant to be short, leave it."""
            ],
            IssueFixer.You,
            """No page has very little text"""),

        ["duplicate-content"] = new(
            """Pages with identical content""",
            [
                """Two or more pages have exactly the same main text, word for word.""",
                """Google shows only one of them, and it may not be the one you'd choose.""",
                """Usually it's one page that can be reached at several addresses, such as a print version or an address with extras on the end."""
            ],
            [
                """Open the pages in each group and decide which address is the one you want found.""",
                """Ask your developer to forward the other addresses to it (a "301 redirect"), or to name it as their main address (a "canonical" link).""",
                """If the pages are meant to differ, give each one text of its own."""
            ],
            IssueFixer.Either,
            """No two pages have identical content"""),

        ["near-duplicate-content"] = new(
            """Pages with almost identical content""",
            [
                """Two or more pages have almost the same main text: only a few words differ.""",
                """Google may treat them as copies and show only one.""",
                """That's fine for versions you don't need found separately, such as one product in several sizes, and then there's nothing to do.""",
                """Pages that should each be found, such as one per town, need text that is clearly their own."""
            ],
            [
                """Open the pages in each group side by side and decide whether each needs to be found on its own.""",
                """If so, give each page text that is clearly its own.""",
                """If not, ask your developer to name one of them as the main version (a "canonical" link), or merge them into one page."""
            ],
            IssueFixer.Either,
            """No two pages have almost identical content"""),

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
            """The site answers at one address only""")
        {
            LooksFor = """A site that answers at more than one address"""
        },

        ["image-missing-alt"] = new(
            """Images with no description""",
            [
                """Some images on these pages have no description: the short text that says what an image shows.""",
                """People who can't see the image rely on it, and Google uses it to understand the image for image search.""",
                """An image that is only decoration needs no words, but should be marked as decoration so screen readers skip it."""
            ],
            [
                """Open the page in your site's editor, select each image and fill in its description box, often called "Alt text".""",
                """Say what the image shows, in a few words, as you would to someone who can't see it.""",
                """Ask your developer to do the same for images in the page design, and to mark decoration as such (an empty "alt")."""
            ],
            IssueFixer.You,
            """No image is missing its description"""),

        ["image-alt-too-long"] = new(
            """Image descriptions that are too long""",
            [
                """Some images on these pages have a description, the text that says what an image shows, of more than {limit:imageAltMaxLength}.""",
                """A screen reader reads the whole description aloud in one go, and its user can't skim it.""",
                """The limit is a rule of thumb, so a description just over it that reads well is fine to leave."""
            ],
            [
                """Open the page in your site's editor, select the image and find its description box, often called "Alt text".""",
                """Cut it to a short phrase that says what the image shows.""",
                """Move longer detail, such as the figures in a chart, into a caption or the text beside the image."""
            ],
            IssueFixer.You,
            """No image description is too long"""),

        ["image-missing-dimensions"] = new(
            """Images with no width and height set""",
            [
                """Some images on these pages don't tell the browser their width and height.""",
                """The browser may not leave the right space, so text can jump down the page as each image arrives.""",
                """That annoys readers and counts against the page in Google's speed measures.""",
                """If your site's design already sets the size another way, there's nothing to do."""
            ],
            [
                """Open one of the pages on a phone and watch whether the text jumps as the images appear.""",
                """If it does, ask your developer to add a width and height to the images in the page design."""
            ],
            IssueFixer.Developer,
            """No image is missing its width and height"""),

        ["image-broken"] = new(
            """Images that don't load""",
            [
                """Some images on these pages don't load: the image file can't be found, or fails with an error.""",
                """Visitors see a gap or a broken-image icon, which makes the page look unfinished.""",
                """An image in the page design, such as a logo, shows broken on every page that uses it."""
            ],
            [
                """Open one of the pages in your browser and look for the gap where the image should be.""",
                """Upload the image again in your site's editor, or choose another one.""",
                """Remove the image from the page if it's no longer needed.""",
                """Ask your developer to fix it in the page design if the same image is missing on most pages."""
            ],
            IssueFixer.You,
            """No image fails to load""")
        {
            OneFix = """That's almost certainly an image in your page design, such as a logo or icon, which every page shares."""
        },

        ["image-too-large"] = new(
            """Large images that slow pages down""",
            [
                """Some pages show images whose files are over {limit:largeImageBytes}.""",
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

        ["hreflang-invalid-code"] = new(
            """Language versions labelled with an invalid code""",
            [
                """These pages list their versions in other languages, but label one with an invalid language or country code.""",
                """Google ignores that entry (an "hreflang" link), or reads only its language, so searchers may be shown the wrong version.""",
                """A common slip is "en-UK": the code for the United Kingdom is "GB"."""
            ],
            [
                """Look at the codes the list shows for each page.""",
                """Send the list to your developer and ask for each code in the form language, then country, such as "en-GB" or "de-AT".""",
                """Ask them to correct it on every page that lists the same versions, not only the ones shown here."""
            ],
            IssueFixer.Developer,
            """No language version is labelled with an invalid code""")
        {
            OneFix = """That's almost certainly the list of language versions in your page design, which every page shares."""
        },

        ["hreflang-missing-self"] = new(
            """Language versions that don't list themselves""",
            [
                """These pages list their versions in other languages, but leave their own address out of the list.""",
                """Google expects every version to name itself as well as the others, and may ignore a list that doesn't.""",
                """Often the page is in its list, but written slightly differently, such as "http" for "https" or without the final slash."""
            ],
            [
                """Send the list to your developer: this is a change to the code of your pages.""",
                """Ask them to add each page's own address to its list of language versions (its "hreflang" links).""",
                """Ask them to write it exactly as the page is addressed, with the same "https", "www" and final slash."""
            ],
            IssueFixer.Developer,
            """No language version leaves itself out of its list"""),

        ["hreflang-missing-return"] = new(
            """Language versions that don't link back""",
            [
                """These pages name versions in other languages that don't name them back.""",
                """Google needs both pages to agree before it trusts the pairing, so searchers may be shown the wrong language.""",
                """It usually means a new language was added to some pages' lists but not to others."""
            ],
            [
                """Open one of the pages and a version it names, to confirm that both exist.""",
                """Send the list to your developer and ask them to give every version the same full list (of "hreflang" links).""",
                """Ask them to make sure each list includes the page itself, at its exact address."""
            ],
            IssueFixer.Developer,
            """No language version is missing its link back""")
        {
            OneFix = """That's almost certainly one entry in the list of language versions in your page design, which every page shares."""
        },

        ["hreflang-broken-target"] = new(
            """Language versions that lead to a broken page""",
            [
                """These pages list a version in another language at an address that doesn't work.""",
                """Searchers sent to that version get an error, and the entry for that language does nothing.""",
                """Usually the version was removed or moved, and the lists (of "hreflang" links) were never updated."""
            ],
            [
                """Open the broken address the list shows in your browser, to see the error.""",
                """If that version has moved, ask your developer to point the entry at its new address.""",
                """If it's gone, ask them to remove the entry from every page that lists it."""
            ],
            IssueFixer.Developer,
            """No language version leads to a broken page""")
        {
            OneFix = """That's almost certainly one entry in the list of language versions in your page design, which every page shares."""
        },

        ["hreflang-to-non-indexable"] = new(
            """Language versions that search engines can't show""",
            [
                """These pages list a version in another language at an address that can't itself appear in search results.""",
                """That address forwards elsewhere, is kept out of search, names another page as its main version, or is blocked by your rules file ("robots.txt").""",
                """Search engines can't show it, so the entry for that language (an "hreflang" link) can't be relied on."""
            ],
            [
                """Open the address the list shows in your browser, and see whether it moves you on to a different address.""",
                """If it does, ask your developer to list that final address instead.""",
                """If it doesn't, ask them to list a version that can appear in search, or to let that one appear."""
            ],
            IssueFixer.Developer,
            """No language version leads to a page search can't show""")
        {
            OneFix = """That's almost certainly one entry in the list of language versions in your page design, which every page shares."""
        },

        ["hreflang-duplicate-code"] = new(
            """Two addresses listed for the same language""",
            [
                """These pages list two or more different addresses for the same language or country.""",
                """Search engines can't tell which one to show searchers in that language.""",
                """It usually means two parts of your site each add a list (of "hreflang" links), such as a plugin and the page design."""
            ],
            [
                """Look at the codes the list shows: each is a language that has more than one address.""",
                """Check whether your site has two translation or SEO plugins that each add language versions, and turn one off.""",
                """Ask your developer to keep one address for each language, if the lists are written in the page design."""
            ],
            IssueFixer.Either,
            """No language is listed at two different addresses""")
        {
            OneFix = """That's almost certainly the list of language versions in your page design, which every page shares.""",
            LooksFor = """Languages listed at more than one address"""
        },

        ["hreflang-missing-x-default"] = new(
            """Language versions with no fallback page""",
            [
                """These pages list their versions in other languages, but name no fallback for everyone else.""",
                """The fallback (called "x-default") is the page search engines show when a searcher's language isn't in the list.""",
                """Google suggests one but doesn't require it, so if your versions cover your visitors there's nothing to do."""
            ],
            [
                """Pick the page for visitors whose language you don't cover: usually your language chooser or your main version.""",
                """Ask your developer to add it to every page's list of language versions, labelled "x-default"."""
            ],
            IssueFixer.Developer,
            """No list of language versions is missing a fallback page"""),

        ["hreflang-outside-head"] = new(
            """Language versions written in the wrong part of the page""",
            [
                """These pages list some or all of their language versions in a part of the page where search engines ignore them.""",
                """The entries there (they're "hreflang" links) don't count, so searchers may be shown the wrong language.""",
                """It usually means something added near the top of the page's code, by a plugin or script, has pushed them out of place."""
            ],
            [
                """Note any plugin, tracking code or banner added to the site lately, as one of them may be the cause.""",
                """Send the list to your developer: the hreflang links must sit in the page's "head" section.""",
                """Ask them to move or remove whatever ends that section early."""
            ],
            IssueFixer.Developer,
            """No language version is written in the wrong part of the page"""),

        ["structured-data-invalid"] = new(
            """Details for search engines that can't be read""",
            [
                """Some pages carry details for search engines, such as a product's price, in code that has a mistake in it.""",
                """Google ignores the broken details (they're called "structured data"), so the page may lose extras such as star ratings in its results.""",
                """One mistake in the page design usually breaks it on every page of that kind."""
            ],
            [
                """Test one of the pages in Google's Rich Results Test, a free tool on the web, to see the error.""",
                """Send the list to your developer: it shows the first error in each page's code (a "JSON-LD" block).""",
                """Ask them to fix the page design that writes it, then test the page again."""
            ],
            IssueFixer.Developer,
            """No details for search engines have a mistake in them""")
        {
            OneFix = """That's almost certainly one mistake in your page design, which writes the same code on every page."""
        },

        ["structured-data-missing-required"] = new(
            """Details for search engines with parts missing""",
            [
                """Some pages describe a product, event or similar for search engines, but leave out a detail Google requires.""",
                """Without it, Google won't add the extras to your search result, such as stars, prices or dates (a "rich result").""",
                """Even complete details only make the extras possible: Google decides whether to show them."""
            ],
            [
                """Look at what the list says is missing on each page, such as "Product: needs name".""",
                """Fill in that detail in your site's editor or shop settings if it's something you enter, such as a price or a date.""",
                """If it isn't, ask your developer to add it to the page's "structured data", then test the page in Google's Rich Results Test."""
            ],
            IssueFixer.Either,
            """No details for search engines are missing a needed part""")
        {
            OneFix = """That's almost certainly your page design, which leaves the same detail out of every page of one kind."""
        },

        ["no-https"] = new(
            """Your site isn't secure (no HTTPS)""",
            [
                """Your site doesn't use HTTPS, the secure connection, so nothing sent to or from it is protected.""",
                """Browsers label every page "Not secure", and anything a visitor types, such as a password, can be read on the way.""",
                """Google prefers the secure version of a page when it has the choice.""",
                """This is one fault for the whole site, and the first thing to fix."""
            ],
            [
                """Open your site with "https://" in front of its address, to see whether a secure version already exists.""",
                """Ask your hosting company to switch on HTTPS: most include a free certificate.""",
                """Ask them, or your developer, to forward every "http" address to its "https" version (a "301 redirect").""",
                """Check the site again, and update any links and settings that still say "http"."""
            ],
            IssueFixer.Developer,
            """The site loads over HTTPS""")
        {
            LooksFor = """A site that isn't secure (no HTTPS)"""
        },

        ["http-page"] = new(
            """Pages still on the insecure address""",
            [
                """These pages still load over "http", without the secure connection the rest of your site uses.""",
                """Browsers label them "Not secure", and if an "https" copy exists too, search engines see two copies of the page.""",
                """Usually an old link or sitemap entry still points at the "http" address, and nothing forwards it on."""
            ],
            [
                """Open one of the addresses in your browser and look for "Not secure" beside it.""",
                """Ask your developer or hosting company to forward every "http" address to its "https" version (a "301 redirect").""",
                """Change the links and sitemap entries that still use "http", so they point at the "https" address."""
            ],
            IssueFixer.Developer,
            """No page is left on the insecure address"""),

        ["mixed-content"] = new(
            """Secure pages that load insecure files""",
            [
                """These secure pages load some of their files, such as images or scripts, over an insecure "http" address.""",
                """Browsers block some of these files (they call it "mixed content"), so parts of the page can break or go missing.""",
                """It's usually an old "http" address left in the page or its design."""
            ],
            [
                """Open one of the pages in your browser and look for missing images, or a warning beside the address.""",
                """Change each address the list shows from "http" to "https", in your site's editor.""",
                """If the file is in your page design, ask your developer to change it there.""",
                """If the other site has no "https" version, copy the file to your own site or replace it."""
            ],
            IssueFixer.Either,
            """No secure page loads an insecure file""")
        {
            OneFix = """That's almost certainly a file your page design loads on every page, such as a script, style file or logo."""
        },

        ["security-headers-missing"] = new(
            """Recommended security settings are missing""",
            [
                """Your site doesn't send some of the settings that tell browsers how to protect visitors (its "security headers").""",
                """They guard against attacks such as someone showing your site inside a fake one.""",
                """Only your home page is checked, as these are set once for the whole site.""",
                """Search isn't affected and many sites run without them, so this can wait until your developer next works on the site."""
            ],
            [
                """Send this to your developer or hosting company: the list names the missing settings.""",
                """Ask them to add the simple ones first (X-Content-Type-Options and Referrer-Policy), and to test the others before switching them on."""
            ],
            IssueFixer.Developer,
            """No recommended security setting is missing""")
        {
            LooksFor = """Missing security settings"""
        },

        ["url-uppercase"] = new(
            """Addresses with capital letters""",
            [
                """These pages have capital letters in their address, such as /About-Us.""",
                """Capitals count in an address: /Page and /page are two different addresses, so a link typed in small letters can miss.""",
                """An address that works and is always linked the same way is fine to leave, and then there's nothing to do."""
            ],
            [
                """Use small letters only in the address of every new page.""",
                """Leave an existing address alone unless it causes broken links or copies of the page.""",
                """If it does, ask your developer to forward it to the small-letter version (a "301 redirect"), and update your links."""
            ],
            IssueFixer.Either,
            """No page address has capital letters"""),

        ["url-underscores"] = new(
            """Addresses with underscores""",
            [
                """These pages have underscores in their address, such as /garden_chairs.""",
                """Google recommends hyphens instead, as in /garden-chairs, because it reads them as gaps between words.""",
                """The gain is small, so there's nothing to do for addresses that already exist."""
            ],
            [
                """Use hyphens between words in the address of every new page.""",
                """Leave existing addresses alone, unless you're changing them for another reason.""",
                """If you do change one, ask your developer to forward the old address to the new one (a "301 redirect")."""
            ],
            IssueFixer.Either,
            """No page address has underscores"""),

        ["url-too-long"] = new(
            """Very long addresses""",
            [
                """These pages have an address of more than {limit:maxUrlLength}.""",
                """Search isn't affected, but long addresses are harder to read, share and type.""",
                """They often come from folders inside folders, or from tracking codes added to links.""",
                """An address that works is fine to leave, and then there's nothing to do."""
            ],
            [
                """Keep the address short and readable when you make a new page.""",
                """Remove tracking codes, such as "?utm_source=newsletter", from links between your own pages.""",
                """Leave existing addresses alone, or ask your developer to forward the old address if you shorten one (a "301 redirect")."""
            ],
            IssueFixer.Either,
            """No page address is very long"""),

        ["slow-response"] = new(
            """Pages that are slow to answer""",
            [
                """These pages took more than {limit:slowResponseMs} to arrive when the check asked for them.""",
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

        ["large-page"] = new(
            """Pages with a lot of code to download""",
            [
                """These pages have more than {limit:largePageBytes} of code in the page itself, before any images or other files.""",
                """A page that heavy takes longer to arrive and to appear, most of all on a phone.""",
                """Google also stops reading a page at 2 MB, which is listed as a problem of its own.""",
                """Below 2 MB, a page that still feels quick on a phone needs nothing done."""
            ],
            [
                """Open one of the pages on a phone and see whether it's slow to appear.""",
                """If it is, send the list to your developer: it shows each page's size.""",
                """Ask them to move scripts, styles and images that are written into the page out into separate files, and to split very long lists."""
            ],
            IssueFixer.Developer,
            """No page has too much code to download"""),

        ["html-past-crawl-limit"] = new(
            """Pages too big for Google to read in full""",
            [
                """These pages have more than 2 MB of code in the page itself, which is as far as Google reads.""",
                """Google ignores everything after that point, so text and links near the end of the page aren't seen.""",
                """The bulk is usually scripts, styles or images written into the page instead of loaded as separate files."""
            ],
            [
                """Send the list to your developer: it shows each page's size.""",
                """Ask them to move scripts, styles and images that are written into the page out into separate files.""",
                """Ask them to put the main text and links early in the page, within the first 2 MB."""
            ],
            IssueFixer.Developer,
            """No page is too big for Google to read in full"""),

        ["viewport-missing"] = new(
            """Pages not set up for phones""",
            [
                """These pages lack the line of code that tells a phone to fit the page to its screen.""",
                """Phones show them as a tiny copy of the desktop page, so visitors must zoom and scroll sideways to read.""",
                """Google reads every page as a phone would, so this is the version it judges."""
            ],
            [
                """Open one of the pages on a phone and see whether the text is too small to read.""",
                """Ask your developer to add the missing line (a "viewport" setting) to the page design.""",
                """Ask them to check the page design really adapts to small screens, as the line alone won't do it."""
            ],
            IssueFixer.Developer,
            """No page is missing its phone-screen setting"""),

        ["lang-missing"] = new(
            """Pages that don't say what language they're in""",
            [
                """These pages don't state in their code which language they're written in.""",
                """Screen readers use that to pick the right voice, so without it they may read the page in the wrong accent.""",
                """Google works the language out from the text itself, so search isn't affected and this can wait."""
            ],
            [
                """Check your site's settings for a "site language" option, and set it if it's empty.""",
                """Ask your developer to add the language to the page design (a "lang" setting, such as "en-GB") if there's no such option."""
            ],
            IssueFixer.Either,
            """Every page says what language it is in"""),

        ["charset-missing"] = new(
            """Pages at risk of showing garbled characters""",
            [
                """These pages don't tell browsers how their letters and symbols were saved.""",
                """Browsers have to guess, and a wrong guess turns accents, £ signs and curly quotes into nonsense.""",
                """Pages that look right today can wait, but the one-line fix removes the risk."""
            ],
            [
                """Open one of the pages and look for odd symbols where accents, £ signs or quote marks should be.""",
                """Ask your developer to state the character set in the page design (a "charset" line, set to "utf-8")."""
            ],
            IssueFixer.Developer,
            """No page leaves browsers guessing its characters"""),

        ["open-graph-missing"] = new(
            """Pages that won't preview well when shared""",
            [
                """These pages lack the title, description or image that apps show when someone shares a link.""",
                """Without them (they're called "Open Graph tags"), the preview is a guess, or a bare link.""",
                """It matters only for pages people are likely to share: for the rest there's nothing to do."""
            ],
            [
                """Find the page's "social sharing" or "social preview" settings in your site's editor.""",
                """Add a title, a short description and an image.""",
                """If there's no such setting, ask your developer to add Open Graph tags to the page design."""
            ],
            IssueFixer.Either,
            """Every page has its sharing preview details""")
        {
            OneFix = """That's almost certainly your page design, which leaves the same details out of every page."""
        }
    };

    /// <summary>Every check that has plain words, by its id.</summary>
    public static IReadOnlyDictionary<string, IssuePlainText> All => BY_CHECK;

    /// <summary>The check's plain words, or null when it has none yet.</summary>
    public static IssuePlainText? Find(string checkId) => BY_CHECK.GetValueOrDefault(checkId);
}
