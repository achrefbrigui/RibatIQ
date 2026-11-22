using System.Text.Json;

using RibatIQ.Application.Analysis;
using RibatIQ.Application.Rules;
using RibatIQ.Domain.Findings;
using RibatIQ.Domain.Results;
using RibatIQ.Infrastructure.Dependencies;

namespace RibatIQ.IntegrationTests;

/// <summary>
/// The NuGet audit: parsing of the real <c>dotnet list package</c> output format (captured from the .NET 10
/// SDK), the real tool on an unrestored project (offline), and an opt-in run against nuget.org.
/// </summary>
public sealed class DependencyAuditTests : IDisposable
{
    private static readonly RuleDefinition Rule = RuleCatalog.Default.Get(DotnetListPackageAuditor.VulnerablePackage);

    private readonly TempDirectory _directory = new();

    public void Dispose() => _directory.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private string Root => _directory.Path;

    private string ProjectPath(string name) => Path.Combine(Root, name, $"{name}.csproj").Replace('\\', '/');

    [Fact]
    public void Parser_VulnerablePackages_BecomeFindingsOnTheProjectFile()
    {
        var report = ListPackageOutputParser.Parse(Output(Project("App", Framework("net10.0",
            top: Package("Newtonsoft.Json", "12.0.1", ("High", "https://github.com/advisories/GHSA-5crp-9r3c-p9vr")),
            transitive: Package("System.Text.Encodings.Web", "4.5.0", ("Critical", "https://github.com/advisories/GHSA-ghhp-997w-qr28"))))), Root, Rule);

        Assert.Equal(["App/App.csproj"], report.AuditedProjects);
        Assert.Empty(report.UnauditedProjects);
        Assert.Equal(2, report.Findings.Count);

        var direct = report.Findings[0];
        Assert.Equal(Rule.Id, direct.RuleId);
        Assert.Equal("Newtonsoft.Json/GHSA-5crp-9r3c-p9vr", direct.ToolRuleId);
        Assert.Equal(FindingSeverity.High, direct.Severity);
        Assert.Equal(FindingCategory.Dependencies, direct.Category);
        Assert.Equal(FindingOrigin.StaticAnalysis, direct.Origin);
        Assert.Equal(ListPackageOutputParser.Source, direct.Source);
        Assert.Equal("App/App.csproj", direct.FilePath);
        Assert.Null(direct.StartLine);
        Assert.Null(direct.Confidence);
        Assert.Equal("Vulnerable package Newtonsoft.Json 12.0.1", direct.Title);
        Assert.Contains("direct dependency, net10.0", direct.Description, StringComparison.Ordinal);
        Assert.Contains("https://github.com/advisories/GHSA-5crp-9r3c-p9vr", direct.Description, StringComparison.Ordinal);
        Assert.Equal("Resolved version 12.0.1 for net10.0.", direct.Evidence);

        var transitive = report.Findings[1];
        Assert.Equal(FindingSeverity.Critical, transitive.Severity);
        Assert.Contains("transitive dependency", transitive.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void Parser_SameAdvisoryForSeveralFrameworks_IsReportedOnceAfterDeduplication()
    {
        var advisory = ("High", "https://github.com/advisories/GHSA-5crp-9r3c-p9vr");
        var report = ListPackageOutputParser.Parse(Output(Project("App",
            Framework("net8.0", top: Package("Newtonsoft.Json", "12.0.1", advisory)),
            Framework("net10.0", top: Package("Newtonsoft.Json", "12.0.1", advisory)))), Root, Rule);

        Assert.Equal(2, report.Findings.Count);
        Assert.Single(FindingDeduplicator.Deduplicate(report.Findings));
    }

    [Fact]
    public void Parser_CleanProject_IsAuditedWithoutFindings()
    {
        var report = ListPackageOutputParser.Parse(Output(Project("Clean")), Root, Rule);

        Assert.Equal(["Clean/Clean.csproj"], report.AuditedProjects);
        Assert.Empty(report.Findings);
    }

    [Fact]
    public void Parser_UnrestoredProject_IsNotAudited_NeverClean()
    {
        // Shape captured from 'dotnet list package --vulnerable --format json --no-restore' on an unrestored project.
        var json = $$"""
            {
              "version": 1,
              "parameters": "--vulnerable --include-transitive",
              "problems": [
                { "project": "{{ProjectPath("Fresh")}}", "level": "error", "text": "No assets file was found for `{{ProjectPath("Fresh")}}`. Please run restore before running this command." }
              ],
              "sources": [ "https://api.nuget.org/v3/index.json" ],
              "projects": [ { "path": "{{ProjectPath("Fresh")}}" }, {{Project("Clean")}} ]
            }
            """;

        var report = ListPackageOutputParser.Parse(json, Root, Rule);

        Assert.Equal(["Clean/Clean.csproj"], report.AuditedProjects);
        var (project, problem) = Assert.Single(report.UnauditedProjects);
        Assert.Equal("Fresh/Fresh.csproj", project);
        Assert.Contains("restore", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void Parser_ProjectOutsideTheRoot_IsSkippedWithAWarning()
    {
        var outside = Path.GetFullPath(Path.Combine(Root, "..", "Elsewhere", "Other.csproj")).Replace('\\', '/');
        var json = $$"""{ "version": 1, "projects": [ { "path": "{{outside}}", "frameworks": [] } ] }""";

        var report = ListPackageOutputParser.Parse(json, Root, Rule);

        Assert.Empty(report.AuditedProjects);
        Assert.Contains(report.Warnings, w => w.Contains("outside the analyzed directory", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{ "version": 2, "projects": [] }""")]
    [InlineData("""{ "projects": [] }""")]
    [InlineData("[]")]
    public void Parser_UnexpectedOutput_IsAFormatError(string json) =>
        Assert.Throws<FormatException>(() => ListPackageOutputParser.Parse(json, Root, Rule));

    [Theory]
    [InlineData("Low", FindingSeverity.Low)]
    [InlineData("Moderate", FindingSeverity.Medium)]
    [InlineData("High", FindingSeverity.High)]
    [InlineData("Critical", FindingSeverity.Critical)]
    [InlineData("Unknown", FindingSeverity.High)]
    public void Parser_MapsAdvisorySeverities(string severity, FindingSeverity expected)
    {
        var report = ListPackageOutputParser.Parse(
            Output(Project("App", Framework("net10.0", top: Package("Pkg", "1.0.0", (severity, "https://example.test/advisories/X-1"))))), Root, Rule);

        Assert.Equal(expected, Assert.Single(report.Findings).Severity);
    }

    [Fact]
    public async Task Auditor_WithoutTargets_IsSkipped()
    {
        var result = await new DotnetListPackageAuditor().AuditAsync(Root, [], Ct);

        Assert.Equal(CheckStatus.Skipped, result.Check.Status);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public async Task Auditor_MissingDotnet_FailsTheCheck_WithoutThrowing()
    {
        var project = _directory.WriteFile("App/App.csproj", "<Project />");

        var result = await new DotnetListPackageAuditor(dotnetExecutable: "ribatiq-dotnet-that-does-not-exist").AuditAsync(Root, [project], Ct);

        Assert.Equal(CheckStatus.Failed, result.Check.Status);
        Assert.Contains("dotnet CLI was not found", result.Check.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Auditor_RealSdk_UnrestoredProject_IsSkipped_AndNothingIsWritten()
    {
        var project = _directory.WriteFile(
            "Fresh/Fresh.csproj",
            """<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>""");
        var before = _directory.Snapshot();

        var result = await new DotnetListPackageAuditor().AuditAsync(Root, [project], Ct);

        Assert.Equal(CheckStatus.Skipped, result.Check.Status);
        Assert.Contains("not restored", result.Check.Reason, StringComparison.Ordinal);
        Assert.Contains("RibatIQ never restores", result.Check.Reason, StringComparison.Ordinal);
        Assert.Empty(result.Findings);
        Assert.Equal(before, _directory.Snapshot());
        Assert.False(Directory.Exists(Path.Combine(Root, "Fresh", "obj")));
    }

    [Fact]
    public async Task Auditor_RealSdk_RestoredVulnerableProject_ReportsTheAdvisory()
    {
        Assert.SkipUnless(
            Environment.GetEnvironmentVariable("RIBATIQ_NETWORK_TESTS") == "1",
            "Set RIBATIQ_NETWORK_TESTS=1 to restore from nuget.org and query vulnerability data.");

        _directory.WriteFile("nuget.config", """
            <?xml version="1.0" encoding="utf-8"?>
            <configuration><packageSources><clear /><add key="nuget.org" value="https://api.nuget.org/v3/index.json" /></packageSources></configuration>
            """);
        var project = _directory.WriteFile("App/App.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework><NuGetAudit>false</NuGetAudit></PropertyGroup>
              <ItemGroup><PackageReference Include="Newtonsoft.Json" Version="12.0.1" /></ItemGroup>
            </Project>
            """);
        await RunDotnetAsync(Root, "restore", project);

        var result = await new DotnetListPackageAuditor().AuditAsync(Root, [project], Ct);

        Assert.Equal(CheckStatus.Completed, result.Check.Status);
        var finding = Assert.Single(result.Findings);
        Assert.Equal("Newtonsoft.Json/GHSA-5crp-9r3c-p9vr", finding.ToolRuleId);
        Assert.Equal("App/App.csproj", finding.FilePath);
    }

    private static async Task RunDotnetAsync(string directory, params string[] args)
    {
        var info = new System.Diagnostics.ProcessStartInfo("dotnet") { WorkingDirectory = directory, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in args)
        {
            info.ArgumentList.Add(arg);
        }

        using var process = System.Diagnostics.Process.Start(info)!;
        await process.WaitForExitAsync(Ct);
        Assert.True(process.ExitCode == 0, await process.StandardError.ReadToEndAsync(Ct));
    }

    private static string Output(params string[] projects) =>
        $$"""{ "version": 1, "parameters": "--vulnerable --include-transitive", "sources": [], "projects": [ {{string.Join(", ", projects)}} ] }""";

    private string Project(string name, params string[] frameworks) =>
        frameworks.Length == 0
            ? $$"""{ "path": "{{ProjectPath(name)}}" }"""
            : $$"""{ "path": "{{ProjectPath(name)}}", "frameworks": [ {{string.Join(", ", frameworks)}} ] }""";

    private static string Framework(string name, string? top = null, string? transitive = null) =>
        $$"""
        {
          "framework": "{{name}}",
          "topLevelPackages": [ {{top}} ],
          "transitivePackages": [ {{transitive}} ]
        }
        """;

    private static string Package(string id, string version, params (string Severity, string Url)[] vulnerabilities) =>
        $$"""
        {
          "id": "{{id}}",
          "requestedVersion": "{{version}}",
          "resolvedVersion": "{{version}}",
          "vulnerabilities": [ {{string.Join(", ", vulnerabilities.Select(v => $"{{ \"severity\": {JsonSerializer.Serialize(v.Severity)}, \"advisoryurl\": {JsonSerializer.Serialize(v.Url)} }}"))}} ]
        }
        """;
}
