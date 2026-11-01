using System.Reflection;

namespace ShoutingIguana.Sidecar;

internal static class SidecarInfo
{
    public static string Version { get; } =
        typeof(SidecarInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? "0.0.0";
}
