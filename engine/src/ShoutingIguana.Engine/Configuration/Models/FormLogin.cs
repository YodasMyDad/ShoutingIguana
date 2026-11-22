namespace ShoutingIguana.Engine.Configuration.Models;

/// <summary>
/// Signs in once, in the crawl's browser, before the first fetch. The cookies that sign-in sets are then sent with
/// the crawl's HTTP requests and with later browser fetches. A crawl file keeps the address and the field selectors,
/// and not the password.
/// </summary>
public sealed record FormLogin
{
    /// <summary>The sign-in page, an absolute http or https URL.</summary>
    public string Url { get; init; } = "";

    public string Username { get; init; } = "";

    /// <summary>Empty in a crawl file. Whoever resumes the crawl supplies it again.</summary>
    public string Password { get; init; } = "";

    /// <summary>A CSS selector for the user name field. Empty uses the first email or text field.</summary>
    public string UsernameField { get; init; } = "";

    /// <summary>A CSS selector for the password field. Empty uses the first password field.</summary>
    public string PasswordField { get; init; } = "";

    /// <summary>A CSS selector for the button that submits the form. Empty uses the form's submit control.</summary>
    public string Submit { get; init; } = "";
}
