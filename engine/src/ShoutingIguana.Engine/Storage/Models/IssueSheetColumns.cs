namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>
/// Which of a check's sheet's columns would hold anything (<see cref="IssueSheetReader"/>), so the workbook leaves out a
/// column that's empty on every row, and one that only ever repeats the address.
/// </summary>
/// <param name="Titles">Some flagged URL has a page title.</param>
/// <param name="Details">Some issue has a detail ("What was found").</param>
/// <param name="RelatedUrls">Some issue names another URL ("Other address").</param>
/// <param name="FinalUrlsElsewhere">Some flagged URL ends somewhere else after its redirects; true without a URL summary to tell.</param>
/// <param name="CanonicalsElsewhere">Some flagged URL names another URL as its canonical.</param>
public sealed record IssueSheetColumns(bool Titles, bool Details, bool RelatedUrls, bool FinalUrlsElsewhere, bool CanonicalsElsewhere);
