namespace ShoutingIguana.Engine.Parsing;

/// <summary>
/// What batch 8's content checks read of a page's text (docs/v2-new-checks.md): its sentences and English syllables for
/// the Flesch reading ease, its words, and the first "lorem ipsum" in it.
/// </summary>
internal static class TextMeasures
{
    private const int PLACEHOLDER_QUOTE_CHARS = 60;

    /// <summary>Words, as the content fingerprint counts them: runs of letters and digits.</summary>
    public static int Words(ReadOnlySpan<char> text)
    {
        var words = 0;
        var inWord = false;
        foreach (var character in text)
        {
            var isWordCharacter = char.IsLetterOrDigit(character);
            if (isWordCharacter && !inWord)
            {
                words++;
            }

            inWord = isWordCharacter;
        }

        return words;
    }

    /// <summary>
    /// Sentences (a run of '.', '!' or '?' after a word, before a space or the end) and English syllables (each word's
    /// vowel groups, y counting as a vowel, a final silent e dropped, at least one a word).
    /// </summary>
    public static (int Sentences, int Syllables) SentencesAndSyllables(ReadOnlySpan<char> text)
    {
        var sentences = 0;
        var syllables = 0;
        var index = 0;
        var afterWord = false;
        while (index < text.Length)
        {
            var character = text[index];
            if (char.IsLetter(character))
            {
                var start = index;
                while (index < text.Length && (char.IsLetter(text[index]) || text[index] == '\''))
                {
                    index++;
                }

                syllables += Syllables(text[start..index]);
                afterWord = true;
                continue;
            }

            if (character is '.' or '!' or '?' && afterWord)
            {
                while (index < text.Length && text[index] is '.' or '!' or '?')
                {
                    index++;
                }

                if (index >= text.Length || char.IsWhiteSpace(text[index]))
                {
                    sentences++;
                }

                afterWord = false;
                continue;
            }

            if (char.IsDigit(character))
            {
                afterWord = true;
            }

            index++;
        }

        return (sentences, syllables);
    }

    /// <summary>The first "lorem ipsum" in the text, any case, quoted from there and cut; null without one.</summary>
    public static string? Placeholder(ReadOnlySpan<char> text)
    {
        // "lorem", then any run of spaces, then "ipsum".
        var at = -1;
        for (var from = 0; from < text.Length;)
        {
            var found = text[from..].IndexOf("lorem", StringComparison.OrdinalIgnoreCase);
            if (found < 0)
            {
                break;
            }

            var start = from + found;
            var after = start + "lorem".Length;
            var spaces = after;
            while (spaces < text.Length && char.IsWhiteSpace(text[spaces]))
            {
                spaces++;
            }

            if (spaces > after && text[spaces..].StartsWith("ipsum", StringComparison.OrdinalIgnoreCase))
            {
                at = start;
                break;
            }

            from = after;
        }

        if (at < 0)
        {
            return null;
        }

        var rest = text[at..];
        var quote = rest.Length > PLACEHOLDER_QUOTE_CHARS ? $"{rest[..PLACEHOLDER_QUOTE_CHARS].TrimEnd()}…" : rest.TrimEnd().ToString();
        return string.Join(' ', quote.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static int Syllables(ReadOnlySpan<char> word)
    {
        var groups = 0;
        var previousVowel = false;
        foreach (var character in word)
        {
            var vowel = char.ToLowerInvariant(character) is 'a' or 'e' or 'i' or 'o' or 'u' or 'y';
            if (vowel && !previousVowel)
            {
                groups++;
            }

            previousVowel = vowel;
        }

        // A final silent e ("make", "phrase"), but not "le" after a consonant ("table") or a word that's all one group.
        if (groups > 1 && word.Length > 2 && char.ToLowerInvariant(word[^1]) == 'e' &&
            !(char.ToLowerInvariant(word[^2]) == 'l' && !IsVowel(word[^3])) && !IsVowel(word[^2]))
        {
            groups--;
        }

        return Math.Max(1, groups);
    }

    private static bool IsVowel(char character) => char.ToLowerInvariant(character) is 'a' or 'e' or 'i' or 'o' or 'u' or 'y';
}
