namespace ShoutingIguana.Engine.Fetching;

/// <summary>A held request slot on a <see cref="HostGate"/>. Dispose to release it.</summary>
internal readonly struct HostGateLease(HostGate gate) : IDisposable
{
    public void Dispose() => gate.Release();
}
