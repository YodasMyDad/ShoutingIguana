using Microsoft.Data.Sqlite;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Engine.Storage.Models;
using ShoutingIguana.Engine.Tests.Support;

namespace ShoutingIguana.Engine.Tests.Crawling;

/// <summary>
/// A recheck recorded as failed, which only a file an earlier engine rechecked holds (a failed recheck is rolled back
/// whole now, and leaves no row): it judged nothing it can stand by, so it has no score after. The real one read "0 of 1
/// fixed. Score 83 → 85": the analysis that followed it, over the file it left half-written, moved the score.
/// </summary>
[Collection(RecheckCrawlsCollection.NAME)]
public sealed class FailedRecheckRecordTests(RecheckCrawls crawls)
{
    [Fact]
    public async Task AFailedRecheckHasNoScoreAfter()
    {
        var path = crawls.Copy(crawls.Then);
        var (_, completed) = await RecheckCrawls.RecheckAsync(path, new RecheckAsk("title-too-long"));
        Assert.NotNull(completed.Result!.ScoreAfter);

        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString()))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = $"UPDATE rechecks SET status = {(int)RecheckStatus.Failed} WHERE id = {completed.Id}";
            Assert.Equal(1, command.ExecuteNonQuery());
        }

        var failed = RecheckReader.Read(path, completed.Id)!;
        Assert.Equal(RecheckStatus.Failed, failed.Status);
        Assert.Equal(completed.Result.ScoreBefore, failed.Result!.ScoreBefore);
        Assert.Null(failed.Result.ScoreAfter);
        Assert.Null(RecheckReader.ReadAll(path).Single(recheck => recheck.Id == completed.Id).Result!.ScoreAfter);
    }
}
