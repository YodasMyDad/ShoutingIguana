EDITS = [
('tests/ShoutingIguana.Sidecar.Tests/GridEndpointsTests.cs',
'''        Assert.Equal($$"""{"inlinks":{{inlinks["total"]}},"outlinks":{{outlinks["total"]}}}""", product["linkTotals"]!.ToJsonString());
''',
'''        var hyperlinks = await PostOkAsync($"api/crawls/{grid.Id}/urls/inlinks", JsonNode.Parse($$"""{"urlId":{{IdOf("/p/7")}},"kinds":["hyperlink"],"limit":1}"""));
        Assert.Equal($$"""{"inlinks":{{inlinks["total"]}},"outlinks":{{outlinks["total"]}},"hyperlinkInlinks":{{hyperlinks["total"]}}}""", product["linkTotals"]!.ToJsonString());

        // The hyperlinks to it land in the links grid: the count there is the inspector's. For the home page, which the
        // site's Cloudflare page links to with every other, it's more than the analysis's inlinks, which leave that page out.
        foreach (var path in new[] { "/p/7", "/" })
        {
            var inspected = path == "/p/7" ? product : await InspectAsync(IdOf(path));
            var listed = await PostOkAsync($"api/crawls/{grid.Id}/links", JsonNode.Parse(
                $$"""{"filters":[{"column":"to.id","op":"eq","value":{{IdOf(path)}}},{"column":"kind","op":"in","values":["hyperlink"]},{"column":"selfLink","op":"is","value":false}],"limit":1}"""));
            Assert.Equal((long)listed["matching"]!, (long)inspected["linkTotals"]!["hyperlinkInlinks"]!);
            Assert.True((long)inspected["linkTotals"]!["hyperlinkInlinks"]! >= (long)inspected["links"]!["inlinks"]!);
        }

        Assert.True((long)(await InspectAsync(IdOf("/")))["linkTotals"]!["hyperlinkInlinks"]! > (long)(await InspectAsync(IdOf("/")))["links"]!["inlinks"]!);
'''),
]
