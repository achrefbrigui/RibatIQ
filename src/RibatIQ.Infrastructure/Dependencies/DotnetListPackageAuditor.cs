using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;

using RibatIQ.Application.Dependencies;
using RibatIQ.Application.Rules;
using RibatIQ.Domain.Findings;
using RibatIQ.Domain.Results;
using RibatIQ.Domain.Rules;
using RibatIQ.Infrastructure.Processes;

namespace RibatIQ.Infrastructure.Dependencies;

/// <summary>
/// Audits NuGet dependencies with <c>dotnet list package --vulnerable --include-transitive --format json
/// --no-restore</c>. RibatIQ never restores: projects that were not restored are reported as not audited.
/// The SDK evaluates the project files and queries the configured NuGet sources for vulnerability data.
/// </summary>
public sealed class DotnetListPackageAuditor : IDependencyAuditor
{
    private const string CheckId = DependencyAudit.CheckId;
    private const string CheckName = DependencyAudit.CheckName;

    public static readonly ReviewRuleId VulnerablePackage = DependencyAudit.VulnerablePackageRule;

    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(5);

    /// <summary>Largest <c>dotnet list package</c> output read, in characters.</summary>
    public const int MaxOutputChars = 16 * 1024 * 1024;

    private const int MaxErrorChars = 64 * 1024;

    private readonly RuleCatalog _catalog;
    private readonly string _dotnet;
    private readonly TimeSpan _timeout;

    public DotnetListPackageAuditor(RuleCatalog? catalog = null, string dotnetExecutable = "dotnet", TimeSpan? timeout = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dotnetExecutable);

        _catalog = catalog ?? RuleCatalog.Default;
        _dotnet = dotnetExecutable;
        _timeout = timeout ?? DefaultTimeout;
    }

    public IReadOnlyList<ReviewRuleId> SupportedRules { get; } = [VulnerablePackage];

    public async Task<DependencyAuditResult> AuditAsync(string rootDirectory, IReadOnlyList<string> targets, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        ArgumentNullException.ThrowIfNull(targets);

        var rule = _catalog.Get(VulnerablePackage);
        if (targets.Count == 0)
        {
            return new DependencyAuditResult([], Skipped("No project or solution file to audit."), []);
        }

        var findings = new List<ReviewFinding>();
        var warnings = new List<string>();
        var audited = new List<string>();
        var unaudited = new List<(string Project, string Problem)>();

        foreach (var target in targets)
        {
            RunResult run;
            try
            {
                run = await RunAsync(Path.GetDirectoryName(target)!, target, cancellationToken).ConfigureAwait(false);
            }
            catch (Win32Exception)
            {
                return Failed("The dotnet CLI was not found on the PATH.", findings, warnings);
            }
            catch (TimeoutException)
            {
                return Failed(string.Create(CultureInfo.InvariantCulture, $"dotnet list package did not finish within {_timeout.TotalMinutes:0} minutes."), findings, warnings);
            }
            catch (FormatException ex)
            {
                return Failed($"{ex.Message}.", findings, warnings);
            }

            ListPackageReport report;
            try
            {
                report = ListPackageOutputParser.Parse(run.Output, rootDirectory, rule);
            }
            catch (FormatException ex)
            {
                return Failed($"{ex.Message} (exit code {run.ExitCode}).", findings, warnings);
            }

            findings.AddRange(report.Findings);
            warnings.AddRange(report.Warnings);
            audited.AddRange(report.AuditedProjects);
            unaudited.AddRange(report.UnauditedProjects);
        }

        var auditedProjects = audited.Distinct(StringComparer.Ordinal).ToArray();
        var notAudited = unaudited.Select(u => u.Project).Distinct(StringComparer.Ordinal).ToArray();

        if (auditedProjects.Length == 0)
        {
            var reason = notAudited.Length > 0
                ? $"Projects are not restored ({string.Join(", ", notAudited.Take(5))}). Run 'dotnet restore' and scan again; RibatIQ never restores."
                : "dotnet list package found no project to audit.";
            return new DependencyAuditResult([], Skipped(reason), warnings);
        }

        if (notAudited.Length > 0)
        {
            var reason = string.Create(CultureInfo.InvariantCulture,
                $"{auditedProjects.Length} of {auditedProjects.Length + notAudited.Length} project(s) audited; not restored: {string.Join(", ", notAudited.Take(5))}.");
            warnings.Add($"Dependency audit is partial: {reason}");
            return new DependencyAuditResult(findings, new CheckResult(CheckId, CheckName, CheckStatus.Completed, reason, SupportedRules), warnings);
        }

        return new DependencyAuditResult(findings, CheckResult.Completed(CheckId, CheckName, SupportedRules), warnings);
    }

    private static CheckResult Skipped(string reason) => DependencyAudit.Skipped(reason);

    private DependencyAuditResult Failed(string reason, List<ReviewFinding> findings, List<string> warnings) =>
        new(findings, CheckResult.Failed(CheckId, CheckName, reason, SupportedRules), warnings);

    private async Task<RunResult> RunAsync(string workingDirectory, string target, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(_dotnet)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            StandardErrorEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
        };

        foreach (var argument in new[]
        {
            "list", target, "package", "--vulnerable", "--include-transitive",
            "--format", "json", "--output-version", "1", "--no-restore",
        })
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.Environment["DOTNET_NOLOGO"] = "1";
        startInfo.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        startInfo.Environment["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1";
        startInfo.Environment["DOTNET_CLI_UI_LANGUAGE"] = "en";

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_timeout);

        using var process = Process.Start(startInfo) ?? throw new Win32Exception("dotnet could not be started.");
        process.StandardInput.Close();

        var output = BoundedOutput.ReadAsync(process.StandardOutput, MaxOutputChars, timeout.Token);
        var error = BoundedOutput.ReadAsync(process.StandardError, MaxErrorChars, timeout.Token);

        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            var (outputText, outputExceeded) = await output.ConfigureAwait(false);
            await error.ConfigureAwait(false);

            // Too much output is reported as a format error: the check fails, nothing is guessed from a prefix.
            return outputExceeded
                ? throw new FormatException(string.Create(CultureInfo.InvariantCulture, $"dotnet list package produced more than {MaxOutputChars:N0} characters of output"))
                : new RunResult(process.ExitCode, outputText);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // Already exited.
            }

            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            throw new TimeoutException();
        }
    }

    private sealed record RunResult(int ExitCode, string Output);
}
