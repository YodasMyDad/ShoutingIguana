using System.Reflection;

namespace ShoutingIguana.Engine;

public static class EngineInfo
{
    public static string Version { get; } =
        typeof(EngineInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? "0.0.0";
}
