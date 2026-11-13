using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Engine.Export;

/// <summary>
/// The robots.txt screen's words for an origin's answer, as its list's "robots.txt" cell shows them (the app's
/// <c>screens/robots/robotsWords.ts</c>, <c>stateWords</c>): what the answer meant to the crawl, and, beside an answer
/// that refuses nothing, that URLs on the origin were refused all the same by an earlier one.
/// </summary>
internal static class RobotsOriginWords
{
    /// <param name="respected">Whether the crawl obeyed robots.txt: false for a crawl told to ignore it, which refused nothing.</param>
    public static string State(RobotsOrigin origin, bool respected)
    {
        var earlier = origin.Blocked > 0;
        return origin.State switch
        {
            RobotsState.Read when origin.Bytes == 0 => "Read: empty",
            RobotsState.Read when !respected => "Read: rules ignored",
            RobotsState.Read => "Read",
            RobotsState.Missing => earlier ? "Missing when last asked" : "Missing: nothing refused",
            RobotsState.Unreachable => respected ? "Couldn’t be read: URLs refused" : "Couldn’t be read: ignored",
            RobotsState.Unanswered when respected && earlier => "No answer when last asked",
            RobotsState.Unanswered => respected ? "No answer: crawled anyway" : "No answer: ignored",
            _ => "None kept"
        };
    }
}
