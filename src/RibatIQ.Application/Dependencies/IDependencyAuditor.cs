using RibatIQ.Domain.Findings;
using RibatIQ.Domain.Results;
using RibatIQ.Domain.Rules;

namespace RibatIQ.Application.Dependencies;

/// <summary>Audits NuGet dependencies for known vulnerabilities with .NET SDK tooling.</summary>
public interface IDependencyAuditor
{
    /// <summary>Catalogue rules the auditor reports.</summary>
    IReadOnlyList<ReviewRuleId> SupportedRules { get; }

    /// <summary>
    /// Audits <paramref name="targets"/> (solution or project files). Never throws for tool problems:
    /// they are reported through <see cref="DependencyAuditResult.Check"/> (skipped or failed).
    /// </summary>
    /// <param name="rootDirectory">Directory that finding paths are made relative to.</param>
    /// <param name="targets">Absolute paths of the solution or project files to audit.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    Task<DependencyAuditResult> AuditAsync(string rootDirectory, IReadOnlyList<string> targets, CancellationToken cancellationToken);
}

/// <summary>Identifiers of the dependency-audit check, shared by the workflows and the auditors.</summary>
public static class DependencyAudit
{
    public const string CheckId = "dependency-audit";
    public const string CheckName = "NuGet vulnerability audit";

    /// <summary>The catalogue rule for known-vulnerable NuGet packages.</summary>
    public static readonly ReviewRuleId VulnerablePackageRule = ReviewRuleId.Parse("DEPENDENCY.NUGET.VULNERABLE");

    public static CheckResult Skipped(string reason) => CheckResult.Skipped(CheckId, CheckName, reason, [VulnerablePackageRule]);
}

/// <summary>Findings and check outcome of a dependency audit.</summary>
public sealed record DependencyAuditResult(IReadOnlyList<ReviewFinding> Findings, CheckResult Check, IReadOnlyList<string> Warnings);
