P = 'tests/ShoutingIguana.Engine.Tests/Storage/LinkPassSplitTests.cs'

EDITS = [
(P,
'''                    Assert.True(one.SequenceEqual(several), $"{name}, {sort?.Column ?? "crawl order"}, {System.IO.Path.GetFileName(path)}: {one.Count} links on one connection, {several.Count} on {helpers + 1}");
                    Assert.NotEmpty(split);
                    Assert.Contains(helpers + 1, split);
''',
'''                    Assert.True(one.SequenceEqual(several), $"{name}, {sort?.Column ?? "crawl order"}, {System.IO.Path.GetFileName(path)}: {one.Count} links on one connection, {several.Count} on {helpers + 1}");

                    // Every link was read, on every connection lent: unless the filters name few enough targets for the links'
                    // index to find their links, as this small crawl's are, which reads only those and is no pass.
                    if (!filters.Any(filter => filter.Column.StartsWith("to.", StringComparison.Ordinal)))
                    {
                        Assert.True(split.Contains(helpers + 1), $"{name}, {sort?.Column ?? "crawl order"}, {System.IO.Path.GetFileName(path)}: read on {string.Join(", ", split)} connections");
                    }
'''),
(P,
'''        Assert.Equal((["filters[0].value"], ReaderFunctions.TOO_COMPLEX), (refused.Errors.Keys, refused.Errors["filters[0].value"][0]));
''',
'''        Assert.Equal(["filters[0].value"], refused.Errors.Keys);
        Assert.Equal(ReaderFunctions.TOO_COMPLEX, refused.Errors["filters[0].value"][0]);
'''),
]
