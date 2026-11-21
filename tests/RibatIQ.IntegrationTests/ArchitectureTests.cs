using System.Reflection;

namespace RibatIQ.IntegrationTests;

/// <summary>
/// Guards the dependency direction: Domain &lt;- Application &lt;- Infrastructure &lt;- Cli.
/// </summary>
public sealed class ArchitectureTests
{
    [Fact]
    public void Domain_DependsOnNoOtherRibatIqAssembly_AndNoThirdPartyPackages()
    {
        var references = GetReferencedAssemblyNames("RibatIQ.Domain");

        Assert.All(references, name => Assert.True(
            IsFrameworkAssembly(name),
            $"RibatIQ.Domain must only reference the .NET base library, but references '{name}'."));
    }

    [Fact]
    public void Application_DependsOnlyOnDomain_AmongRibatIqAssemblies()
    {
        var ribatReferences = GetReferencedAssemblyNames("RibatIQ.Application")
            .Where(name => name.StartsWith("RibatIQ.", StringComparison.Ordinal));

        Assert.All(ribatReferences, name => Assert.Equal("RibatIQ.Domain", name));
    }

    [Theory]
    [InlineData("RibatIQ.Domain")]
    [InlineData("RibatIQ.Application")]
    public void CoreLayers_DoNotReferenceCliOrProviderSdks(string assemblyName)
    {
        var forbiddenPrefixes = new[] { "System.CommandLine", "OpenAI", "Anthropic", "Octokit", "RibatIQ.Infrastructure", "ribat" };
        var references = GetReferencedAssemblyNames(assemblyName);

        foreach (var reference in references)
        {
            Assert.DoesNotContain(forbiddenPrefixes, prefix => reference.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        }
    }

    private static List<string> GetReferencedAssemblyNames(string assemblyName) =>
        Assembly.Load(assemblyName)
            .GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .ToList();

    private static bool IsFrameworkAssembly(string name) =>
        name is "netstandard" or "mscorlib"
        || name.StartsWith("System", StringComparison.Ordinal)
        || name.StartsWith("Microsoft.Win32", StringComparison.Ordinal);
}
