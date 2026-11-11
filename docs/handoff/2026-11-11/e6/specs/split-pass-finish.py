C = 'src/ShoutingIguana.Cli/Benchmarking/ReaderBenchmark.cs'
T = 'tests/ShoutingIguana.Cli.Tests/CliTests.cs'
D = '../docs/v2-sidecar-api.md'

EDITS = [
(C,
'''        ["link-pass-split"] = 2_500,
''',
'''        ["link-pass-split"] = 1_500,
'''),
(C,
'''    /// <item>Search as it's typed (<c>search</c>) passes over the crawl's URLs and titles in memory: 1.5 to 2.8 ms at 100k
''',
'''    /// <item>The same passes split by link id over the connections a host lends (<c>link-pass-split</c>: four in all) take
    /// about a third of the time: measured on a desktop other work was loading, a text filter 294 ms where one connection
    /// took 870, rare text 175 for 478, a search 294 for 897, a filter on the source's depth 333 for 1,478. Held to
    /// 1,500 ms, <b>a provisional figure</b>: the worst p95 of that one loaded run was 978 ms, and no quiet run or CI run
    /// has yet given a better one.</item>
    /// <item>Search as it's typed (<c>search</c>) passes over the crawl's URLs and titles in memory: 1.5 to 2.8 ms at 100k
'''),
(T,
'''        Assert.Equal(["link-grid", "link-pass", "link-targets"], result["cases"]!.AsArray().Where(read => (string)read!["kind"]! == "links-grid" || ((string)read["name"]!).StartsWith("Links grid: facets", StringComparison.Ordinal))
''',
'''        Assert.Equal(["link-grid", "link-pass", "link-pass-split", "link-targets"], result["cases"]!.AsArray().Where(read => (string)read!["kind"]! == "links-grid" || ((string)read["name"]!).StartsWith("Links grid: facets", StringComparison.Ordinal))
'''),
(D,
'''**Without the link summary, or beyond it, every link is read once per filter set.** That is: a crawl that's running,
''',
'''**A pass over every link is split over several connections** (E6) where the sidecar has read slots free: the links
are divided by id into as many stretches as there are connections (four at most: the read's own and up to three
more), each read on a thread of its own, and what each kept and counted is put together. The answer is the one
connection's, to the link. Measured on the 100k crawl on a machine other work was loading, the first read of a view
took about a third of the time: a text filter 294 ms where one connection took 870, rare text 175 for 478, a pattern
396 for 1,253, a search 294 for 897, a filter on the source's depth 333 to 476 for 934 to 1,478. So those reads come
inside or near the 300 ms budget when cores are free, and are as they were when none is.

- **No more read slots than the rule allows.** A connection more is a read slot more, taken only from those free at
  that moment, never waited for, and never the last one free, so a read arriving meanwhile starts at once; with none
  free the pass runs on its own connection. On a two-core machine it never splits.
- **A running crawl's pass is never split.** It stays on the read's one connection and one snapshot, which is the
  moment it answers for, so the time a running crawl's log goes unfolded is what it was.
- **Only for crawls worth it**: from 500,000 links. Nothing is stored for it, in the file or by the analysis.
- **A writer still never waits.** Each connection is a read like any other to an analysis or a continue starting:
  all are stopped together, and the read runs again after.
- A regular expression's time limit (3 s for a read's matches) is each connection's own, so a pattern near the limit
  on one connection may pass on four.

**Without the link summary, or beyond it, every link is read once per filter set.** That is: a crawl that's running,
'''),
]
