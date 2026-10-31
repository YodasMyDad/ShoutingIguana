namespace ShoutingIguana.Engine.Fetching.Models;

/// <summary>How a host responded, fed back into its gate to speed up or back off.</summary>
internal enum HostSignal
{
    Success,

    /// <summary>429, 503 or a block page: the host wants us to slow down.</summary>
    Throttled,

    /// <summary>Timeouts, connection failures and 5xx: the host may be struggling.</summary>
    Failed
}
