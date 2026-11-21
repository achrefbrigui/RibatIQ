using RibatIQ.Domain.Findings;

namespace RibatIQ.Application.Configuration;

/// <summary>
/// Effective settings for a run, after merging all sources. Never holds secrets: API keys are read
/// by provider adapters directly from the environment.
/// </summary>
/// <param name="AiEnabled">Whether AI analysis runs. When <see langword="false"/>, <paramref name="Provider"/> and <paramref name="Model"/> are <see langword="null"/>.</param>
/// <param name="Provider">Canonical provider ID (see <see cref="LlmProviders"/>) when AI is enabled.</param>
/// <param name="Model">Model identifier, or <see langword="null"/> to use the provider adapter's default.</param>
/// <param name="FailOn">Failure threshold applied after the report is produced.</param>
/// <param name="Include">Globs of files to analyze; empty means every supported file.</param>
/// <param name="Exclude">Globs of files to skip; always contains <see cref="SettingsResolver.DefaultExcludes"/>.</param>
public sealed record RibatSettings(
    bool AiEnabled,
    string? Provider,
    string? Model,
    FailPolicy FailOn,
    IReadOnlyList<string> Include,
    IReadOnlyList<string> Exclude)
{
    /// <summary>Review profile from the rule catalogue; <see langword="null"/> for <c>default</c>.</summary>
    public string? Profile { get; init; }

    /// <summary>Rule IDs enabled in addition to the profile.</summary>
    public IReadOnlyList<string> EnableRules { get; init; } = [];

    /// <summary>Rule IDs disabled from the profile.</summary>
    public IReadOnlyList<string> DisableRules { get; init; } = [];

    /// <summary>Findings below this severity are not reported.</summary>
    public FindingSeverity MinimumSeverity { get; init; } = FindingSeverity.Info;

    /// <summary>Project instructions file to use instead of <c>review-rules.md</c> at the root; <see langword="null"/> for the default.</summary>
    public string? InstructionsPath { get; init; }

    /// <summary>Whether NuGet dependencies are audited for known vulnerabilities (scan only, opt-in).</summary>
    public bool AuditDependencies { get; init; }
}
