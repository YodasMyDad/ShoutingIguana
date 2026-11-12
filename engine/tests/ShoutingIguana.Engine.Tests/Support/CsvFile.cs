using System.Text;

namespace ShoutingIguana.Engine.Tests.Support;

/// <summary>
/// A CSV file read back as RFC 4180 says to read one, by a reader written for the tests (not the writer's code run
/// backwards): UTF-8 behind a byte-order mark, rows ended CRLF, a field in quotes holding commas, line breaks and doubled
/// quotes.
/// </summary>
internal static class CsvFile
{
    /// <summary>Every row's fields, the header's first. Fails when the file isn't the CSV the export says it writes.</summary>
    public static List<string[]> Read(string path)
    {
        var bytes = File.ReadAllBytes(path);
        Assert.True(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF, "The file doesn't start with a UTF-8 byte-order mark.");
        var text = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes, 3, bytes.Length - 3);
        return Parse(text);
    }

    public static List<string[]> Parse(string text)
    {
        List<string[]> rows = [];
        List<string> fields = [];
        var field = new StringBuilder();
        var index = 0;
        while (index < text.Length)
        {
            if (text[index] == '"')
            {
                // A quoted field: everything to the closing quote, a doubled quote standing for one.
                index++;
                while (true)
                {
                    Assert.True(index < text.Length, "A quoted field never closes.");
                    if (text[index] != '"')
                    {
                        field.Append(text[index++]);
                    }
                    else if (index + 1 < text.Length && text[index + 1] == '"')
                    {
                        field.Append('"');
                        index += 2;
                    }
                    else
                    {
                        index++;
                        break;
                    }
                }

                Assert.True(index == text.Length || text[index] is ',' or '\r', $"Something follows a closing quote at {index}.");
            }
            else
            {
                while (index < text.Length && text[index] is not (',' or '\r'))
                {
                    Assert.False(text[index] is '"' or '\n', $"A quote or a bare line feed in a field without quotes, at {index}.");
                    field.Append(text[index++]);
                }
            }

            fields.Add(field.ToString());
            field.Clear();
            if (index < text.Length && text[index] == ',')
            {
                index++;
                Assert.True(index < text.Length, "The file ends on a comma.");
                continue;
            }

            Assert.True(index + 1 < text.Length && text[index] == '\r' && text[index + 1] == '\n', $"A row doesn't end CRLF, at {index}.");
            index += 2;
            rows.Add([.. fields]);
            fields.Clear();
        }

        return rows;
    }
}
