using AngleSharp.Dom;
using AngleSharp.Text;
using ShoutingIguana.Engine.Parsing.Models;

namespace ShoutingIguana.Engine.Parsing;

/// <summary>
/// Notes, as the parser creates elements, where the body began and the first element made in it, so a page whose canonical
/// or hreflang ended up outside the head can say what ended the head and on which line (<see cref="HeadEndFact"/>). The
/// parser calls <see cref="Created"/> for every element; it keeps two values, so it costs nothing to speak of. One a thread,
/// as the parser is.
/// </summary>
internal sealed class HeadEndRecorder
{
    private const int MAX_TAG_CHARS = 60;

    private bool _inBody;
    private int? _headLine;
    private int _bodyLine;
    private IElement? _firstInBody;
    private int _firstInBodyLine;

    /// <summary>The parser made the body up, for something that can't be in a head, rather than reading a &lt;body&gt; tag.</summary>
    public bool BodyImplied => _inBody && _bodyLine == 0;

    /// <summary>The parser made the head up, for content before any &lt;head&gt; tag, rather than reading one.</summary>
    public bool HeadImplied => _headLine == 0;

    /// <summary>Starts a new document.</summary>
    public void Reset()
    {
        _inBody = false;
        _headLine = null;
        _bodyLine = 0;
        _firstInBody = null;
        _firstInBodyLine = 0;
    }

    /// <summary>The parser created an element at this place in the source: line 0 for one it made up (an implied body).</summary>
    public void Created(IElement element, TextPosition position)
    {
        if (_inBody)
        {
            if (_firstInBody is null)
            {
                _firstInBody = element;
                _firstInBodyLine = position.Line;
            }
        }
        else if (element.LocalName == "head")
        {
            _headLine ??= position.Line;
        }
        else if (element.LocalName == "body")
        {
            _inBody = true;
            _bodyLine = position.Line;
        }
    }

    /// <summary>What ended the head of the document just parsed, or null without a body.</summary>
    public HeadEndFact? HeadEnd(IDocument document)
    {
        if (document.Body is not { } body)
        {
            return null;
        }

        if (_bodyLine > 0)
        {
            return new HeadEndFact("<body>", _bodyLine, Implicit: false);
        }

        // The parser made the body up for the first thing that can't be in a head: the body's first node.
        foreach (var node in body.ChildNodes)
        {
            switch (node)
            {
                case IElement element:
                    return new HeadEndFact(StartTag(element), element == _firstInBody && _firstInBodyLine > 0 ? _firstInBodyLine : null, Implicit: true);
                case IText text when !string.IsNullOrWhiteSpace(text.Data):
                    var words = string.Join(' ', text.Data.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
                    return new HeadEndFact($"text \"{Cut(words, MAX_TAG_CHARS - 7)}\"", null, Implicit: true);
            }
        }

        return null;
    }

    private static string StartTag(IElement element)
    {
        var tag = $"<{element.LocalName}{string.Concat(element.Attributes.Select(attribute => $" {attribute.Name}=\"{attribute.Value}\""))}>";
        return tag.Length <= MAX_TAG_CHARS ? tag : tag[..(MAX_TAG_CHARS - 2)] + "…>";
    }

    private static string Cut(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "…";
}
