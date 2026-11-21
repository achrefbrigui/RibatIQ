using System.Text.Json;

using RibatIQ.Application.Rules;
using RibatIQ.Domain.Findings;

namespace RibatIQ.Infrastructure.Dependencies;

/// <summary>What one <c>dotnet list package --vulnerable --format json</c> run reported.</summary>
/// <param name="Findings">One finding per vulnerable package and advisory in each audited project.</param>
/// <param name="AuditedProjects">Projects (repository-relative) the tool audited.</param>
/// <param name="UnauditedProjects">Projects the tool could not audit, with the tool's message.</param>
/// <param name="Warnings">Non-fatal issues met while reading the output.</param>
internal sealed record ListPackageReport(
    IReadOnlyList<ReviewFinding> Findings,
    IReadOnlyList<string> AuditedProjects,
    IReadOnlyList<(string Project, string Problem)> UnauditedProjects,
    IReadOnlyList<string> Warnings);

/// <summary>
/// Parses the JSON output (output version 1) of <c>dotnet list package --vulnerable --include-transitive
/// --format json</c>. Projects listed under <c>problems</c> (typically "not restored") are reported as not
/// audited, never as clean.
/// </summary>
internal static class ListPackageOutputParser
{
    public const string Source = "nuget-audit";

    /// <exception cref="FormatException">The output is not the expected JSON document.</exception>
    public static ListPackageReport Parse(string json, string rootDirectory, RuleDefinition rule)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32 });
        }
        catch (JsonException ex)
        {
            throw new FormatException("dotnet list package did not return valid JSON.", ex);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("version", out var version) || version.ValueKind != JsonValueKind.Number || version.GetInt32() != 1)
            {
                throw new FormatException("dotnet list package returned an unsupported output version.");
            }

            var warnings = new List<string>();
            var problems = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var problem in Array(root, "problems"))
            {
                var level = Text(problem, "level");
                var project = Text(problem, "project");
                var message = Text(problem, "text") ?? "unknown problem";

                if (string.Equals(level, "error", StringComparison.OrdinalIgnoreCase) && project is not null)
                {
                    problems[Normalize(project)] = message;
                }
                else
                {
                    warnings.Add($"dotnet list package: {message}");
                }
            }

            var findings = new List<ReviewFinding>();
            var audited = new List<string>();
            var unaudited = new List<(string, string)>();

            foreach (var project in Array(root, "projects"))
            {
                var fullPath = Text(project, "path");
                if (fullPath is null)
                {
                    continue;
                }

                if (!TryRelative(rootDirectory, fullPath, out var relative))
                {
                    warnings.Add($"Skipped audit results for a project outside the analyzed directory: {Path.GetFileName(fullPath)}");
                    continue;
                }

                if (problems.TryGetValue(Normalize(fullPath), out var problem))
                {
                    unaudited.Add((relative, problem));
                    continue;
                }

                audited.Add(relative);

                foreach (var framework in Array(project, "frameworks"))
                {
                    var frameworkName = Text(framework, "framework") ?? "unknown framework";
                    foreach (var (kind, packages) in new[] { ("direct", "topLevelPackages"), ("transitive", "transitivePackages") })
                    {
                        foreach (var package in Array(framework, packages))
                        {
                            findings.AddRange(ToFindings(package, kind, frameworkName, relative, rule));
                        }
                    }
                }
            }

            return new ListPackageReport(findings, audited, unaudited, warnings);
        }
    }

    private static IEnumerable<ReviewFinding> ToFindings(JsonElement package, string kind, string framework, string projectPath, RuleDefinition rule)
    {
        var id = Text(package, "id");
        var version = Text(package, "resolvedVersion") ?? Text(package, "requestedVersion") ?? "unknown version";
        if (id is null)
        {
            yield break;
        }

        foreach (var vulnerability in Array(package, "vulnerabilities"))
        {
            var url = Text(vulnerability, "advisoryurl") ?? Text(vulnerability, "advisoryUrl");
            var severityText = Text(vulnerability, "severity") ?? "unknown";
            var advisory = AdvisoryId(url);

            yield return ReviewFinding.Create(
                title: $"Vulnerable package {id} {version}",
                description: $"{id} {version} ({kind} dependency, {framework}) has a known {severityText.ToLowerInvariant()} severity vulnerability" +
                    (url is null ? "." : $": {url}"),
                severity: MapSeverity(severityText) ?? rule.DefaultSeverity,
                category: rule.Category,
                filePath: projectPath,
                startLine: null,
                endLine: null,
                suggestedFix: $"Upgrade {id} to a version that fixes the advisory, or override the vulnerable transitive version.",
                confidence: null,
                origin: FindingOrigin.StaticAnalysis,
                source: Source,
                toolRuleId: $"{id}/{advisory}",
                ruleId: rule.Id,
                evidence: $"Resolved version {version} for {framework}.");
        }
    }

    internal static FindingSeverity? MapSeverity(string severity) => severity.Trim().ToUpperInvariant() switch
    {
        "LOW" => FindingSeverity.Low,
        "MODERATE" or "MEDIUM" => FindingSeverity.Medium,
        "HIGH" => FindingSeverity.High,
        "CRITICAL" => FindingSeverity.Critical,
        _ => null,
    };

    private static string AdvisoryId(string? url)
    {
        var last = url?.TrimEnd('/').Split('/').LastOrDefault();
        return string.IsNullOrWhiteSpace(last) ? "unknown-advisory" : last.Length <= 60 ? last : last[..60];
    }

    private static bool TryRelative(string rootDirectory, string fullPath, out string relative)
    {
        relative = Path.GetRelativePath(rootDirectory, Path.GetFullPath(fullPath)).Replace('\\', '/');
        try
        {
            relative = RepositoryPath.Normalize(relative);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static string Normalize(string path) => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static JsonElement[] Array(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array
            ? [.. value.EnumerateArray()]
            : [];
}
