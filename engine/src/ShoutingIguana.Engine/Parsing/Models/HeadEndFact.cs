namespace ShoutingIguana.Engine.Parsing.Models;

/// <summary>
/// What made the parser leave a page's head before a canonical or hreflang link in it: an element or text that belongs
/// in the body (a tracking pixel's &lt;img&gt;, a stray &lt;div&gt;), which starts the body there and then, or the page's own
/// &lt;body&gt; tag, the link being written in the body.
/// </summary>
/// <param name="Element">
/// Its start tag as written, its attributes in order, cut to 60 characters (<c>&lt;img src="/t.gif"&gt;</c>); text, quoted
/// (<c>text "Welcome"</c>); or <c>&lt;body&gt;</c>.
/// </param>
/// <param name="Line">The line of the page it's on, from 1; null where the parser doesn't say (text).</param>
/// <param name="Implicit">The parser started the body at it, the head not having been closed: an element or text in the wrong place.</param>
public sealed record HeadEndFact(string Element, int? Line, bool Implicit);
