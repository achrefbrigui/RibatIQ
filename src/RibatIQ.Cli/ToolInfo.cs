using System.Reflection;

namespace RibatIQ.Cli;

/// <summary>Information about the running RibatIQ build.</summary>
internal static class ToolInfo
{
    public static string Version { get; } =
        typeof(ToolInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(ToolInfo).Assembly.GetName().Version?.ToString()
        ?? "0.0.0";
}
