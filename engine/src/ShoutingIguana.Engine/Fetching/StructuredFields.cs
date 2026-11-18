using System.Globalization;
using ShoutingIguana.Engine.Fetching.Models;

namespace ShoutingIguana.Engine.Fetching;

/// <summary>
/// Reads an RFC 8941 dictionary, the form <c>Signature-Input</c> and <c>Signature</c> (RFC 9421) take: members
/// <c>key=value;param=value</c> separated by commas, each value an item or an inner list of items. Strict, as the RFC
/// asks of a parser: anything outside the grammar (a line break, an uppercase key, an unclosed list) fails the whole
/// field, so a value that could add a header never gets as far as a request.
/// </summary>
internal static class StructuredFields
{
    private const string BASE64 = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/=";
    private const string TOKEN_PUNCTUATION = "!#$%&'*+-.^_`|~:/";
    private const int MAX_INTEGER_DIGITS = 15;

    public static bool TryParseDictionary(string text, out IReadOnlyList<StructuredMember> members)
    {
        var reader = new Reader(text.Trim(' '));
        List<StructuredMember> parsed = [];
        members = parsed;
        if (reader.AtEnd)
        {
            return false;
        }

        while (true)
        {
            if (!reader.TryReadKey(out var key))
            {
                return false;
            }

            StructuredMember member;
            if (reader.TryTake('='))
            {
                if (reader.Peek == '(')
                {
                    if (!reader.TryReadInnerList(out var list) || !reader.TryReadParameters(out var listParameters))
                    {
                        return false;
                    }

                    member = new StructuredMember(key, StructuredItemKind.InnerList, null, list, listParameters);
                }
                else
                {
                    if (!reader.TryReadBareItem(out var kind, out var item) || !reader.TryReadParameters(out var itemParameters))
                    {
                        return false;
                    }

                    member = new StructuredMember(key, kind, item, [], itemParameters);
                }
            }
            else
            {
                if (!reader.TryReadParameters(out var flagParameters))
                {
                    return false;
                }

                member = new StructuredMember(key, StructuredItemKind.Boolean, "?1", [], flagParameters);
            }

            parsed.Add(member);
            reader.SkipOptionalWhitespace();
            if (reader.AtEnd)
            {
                return true;
            }

            if (!reader.TryTake(','))
            {
                return false;
            }

            reader.SkipOptionalWhitespace();
            if (reader.AtEnd)
            {
                // A trailing comma.
                return false;
            }
        }
    }

    private sealed class Reader(string text)
    {
        private int _position;

        public bool AtEnd => _position >= text.Length;

        public char Peek => AtEnd ? '\0' : text[_position];

        public bool TryTake(char expected)
        {
            if (Peek != expected || AtEnd)
            {
                return false;
            }

            _position++;
            return true;
        }

        public void SkipOptionalWhitespace()
        {
            while (Peek is ' ' or '\t' && !AtEnd)
            {
                _position++;
            }
        }

        private void SkipSpaces()
        {
            while (Peek == ' ' && !AtEnd)
            {
                _position++;
            }
        }

        public bool TryReadKey(out string key)
        {
            var start = _position;
            if (!(char.IsAsciiLetterLower(Peek) || Peek == '*'))
            {
                key = string.Empty;
                return false;
            }

            _position++;
            while (!AtEnd && (char.IsAsciiLetterLower(Peek) || char.IsAsciiDigit(Peek) || Peek is '_' or '-' or '.' or '*'))
            {
                _position++;
            }

            key = text[start.._position];
            return true;
        }

        public bool TryReadInnerList(out IReadOnlyList<string> items)
        {
            List<string> list = [];
            items = list;
            if (!TryTake('('))
            {
                return false;
            }

            while (true)
            {
                SkipSpaces();
                if (TryTake(')'))
                {
                    return true;
                }

                if (!TryReadBareItem(out _, out var item) || !TryReadParameters(out _))
                {
                    return false;
                }

                list.Add(item);
                if (Peek is not (' ' or ')') || AtEnd)
                {
                    return false;
                }
            }
        }

        public bool TryReadParameters(out IReadOnlyDictionary<string, (StructuredItemKind Kind, string Value)> parameters)
        {
            Dictionary<string, (StructuredItemKind Kind, string Value)> read = new(StringComparer.Ordinal);
            parameters = read;
            while (TryTake(';'))
            {
                SkipSpaces();
                if (!TryReadKey(out var key))
                {
                    return false;
                }

                if (TryTake('='))
                {
                    if (!TryReadBareItem(out var kind, out var value))
                    {
                        return false;
                    }

                    read[key] = (kind, value);
                }
                else
                {
                    read[key] = (StructuredItemKind.Boolean, "?1");
                }
            }

            return true;
        }

        public bool TryReadBareItem(out StructuredItemKind kind, out string item)
        {
            kind = StructuredItemKind.Token;
            item = string.Empty;
            var first = Peek;
            if (AtEnd)
            {
                return false;
            }

            if (first == '-' || char.IsAsciiDigit(first))
            {
                return TryReadNumber(out kind, out item);
            }

            if (first == '"')
            {
                kind = StructuredItemKind.String;
                return TryReadString(out item);
            }

            if (first == ':')
            {
                kind = StructuredItemKind.ByteSequence;
                return TryReadByteSequence(out item);
            }

            if (first == '?')
            {
                kind = StructuredItemKind.Boolean;
                _position++;
                if (Peek is not ('0' or '1') || AtEnd)
                {
                    return false;
                }

                item = $"?{text[_position++]}";
                return true;
            }

            if (char.IsAsciiLetter(first) || first == '*')
            {
                var start = _position++;
                while (!AtEnd && (char.IsAsciiLetterOrDigit(Peek) || TOKEN_PUNCTUATION.Contains(Peek)))
                {
                    _position++;
                }

                item = text[start.._position];
                return true;
            }

            return false;
        }

        private bool TryReadNumber(out StructuredItemKind kind, out string item)
        {
            kind = StructuredItemKind.Integer;
            var start = _position;
            TryTake('-');
            var digits = 0;
            var fraction = -1;
            while (!AtEnd && (char.IsAsciiDigit(Peek) || Peek == '.' && fraction < 0))
            {
                if (Peek == '.')
                {
                    fraction = 0;
                }
                else if (fraction >= 0)
                {
                    fraction++;
                }
                else
                {
                    digits++;
                }

                _position++;
            }

            item = text[start.._position];
            if (fraction >= 0)
            {
                kind = StructuredItemKind.Decimal;
                return digits is > 0 and <= 12 && fraction is > 0 and <= 3;
            }

            return digits is > 0 and <= MAX_INTEGER_DIGITS &&
                   long.TryParse(item, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _);
        }

        private bool TryReadString(out string item)
        {
            var value = new System.Text.StringBuilder();
            item = string.Empty;
            _position++;
            while (!AtEnd)
            {
                var character = text[_position++];
                if (character == '\\')
                {
                    if (AtEnd || Peek is not ('"' or '\\'))
                    {
                        return false;
                    }

                    value.Append(text[_position++]);
                }
                else if (character == '"')
                {
                    item = value.ToString();
                    return true;
                }
                else if (character is < ' ' or > '~')
                {
                    return false;
                }
                else
                {
                    value.Append(character);
                }
            }

            return false;
        }

        private bool TryReadByteSequence(out string item)
        {
            item = string.Empty;
            var start = ++_position;
            while (!AtEnd && BASE64.Contains(Peek))
            {
                _position++;
            }

            if (!TryTake(':'))
            {
                return false;
            }

            item = text[start..(_position - 1)];
            return true;
        }
    }
}
