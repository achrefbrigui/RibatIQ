using RibatIQ.Application.Configuration;
using RibatIQ.Application.Instructions;
using RibatIQ.Application.Rules;
using RibatIQ.Domain.Findings;
using RibatIQ.Domain.Policies;
using RibatIQ.Domain.Results;
using RibatIQ.Domain.Rules;

namespace RibatIQ.Application.Analysis;

/// <summary>Steps shared by the scan and review workflows: policy, checks and normalization.</summary>
internal static class ReviewPipeline
{
    public const string StaticCheckId = "static-analysis";
    public const string AiCheckId = "ai-review";

    /// <exception cref="ConfigurationException">The profile or a rule in the settings is unknown.</exception>
    public static ReviewPolicy ResolvePolicy(RibatSettings settings, RuleCatalog catalog) =>
        ReviewPolicyResolver.Resolve(
            catalog, settings.Profile, settings.EnableRules, settings.DisableRules, settings.MinimumSeverity);

    /// <summary>Loads explicit instructions from disk, or the default file through <paramref name="loadDefault"/>.</summary>
    public static async Task<InstructionsLoadResult?> LoadInstructionsAsync(
        RibatSettings settings,
        IInstructionsReader? reader,
        Func<CancellationToken, Task<InstructionsLoadResult?>> loadDefault,
        CancellationToken cancellationToken)
    {
        if (settings.InstructionsPath is { } path)
        {
            if (reader is null)
            {
                throw new OperationFailedException("Project instructions cannot be read in this configuration.");
            }

            var read = await reader.ReadFileAsync(path, Path.GetFileName(path), cancellationToken).ConfigureAwait(false);
            return read.Problem == InstructionsStatus.NotFound
                ? throw new InvalidInputException($"The instructions file '{path}' does not exist.")
                : ProjectInstructionsLoader.Load(read);
        }

        return await loadDefault(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The static-analysis check: completed for the enabled rules it covers, skipped when none is enabled.</summary>
    public static CheckResult StaticCheck(IStaticAnalyzer analyzer, ReviewPolicy policy)
    {
        var supported = analyzer.SupportedRules;
        var enabled = supported.Where(policy.IsEnabled).ToArray();

        return supported.Count > 0 && enabled.Length == 0
            ? CheckResult.Skipped(StaticCheckId, "Static analysis", "Every static-analysis rule is disabled by the policy.")
            : CheckResult.Completed(StaticCheckId, "Static analysis", enabled);
    }

    /// <summary>AI-review rules the policy enables, in catalogue order.</summary>
    public static RuleDefinition[] EnabledAiRules(RuleCatalog catalog, ReviewPolicy policy) =>
        [.. catalog.RulesDetectedBy(DetectionMethod.AiReview).Where(r => policy.IsEnabled(r.Id))];

    public const string NoAiRuleReason = "No AI-review rule is enabled by the policy.";

    /// <summary>The AI check when it does not run.</summary>
    public static CheckResult AiSkipped(string reason, IEnumerable<ReviewRuleId>? rules = null) =>
        CheckResult.Skipped(AiCheckId, "AI review", reason, rules);

    /// <summary>The AI check after a run: skipped when nothing could be sent to the model.</summary>
    public static CheckResult AiRan(int filesSent, IEnumerable<ReviewRuleId> rules) =>
        filesSent == 0
            ? AiSkipped("No source could be sent to the model within the configured limits.", rules)
            : CheckResult.Completed(AiCheckId, "AI review", rules);

    /// <summary>De-duplicates, then keeps what the policy reports.</summary>
    public static PolicyOutcome Normalize(IEnumerable<ReviewFinding> findings, ReviewPolicy policy) =>
        policy.Apply(FindingDeduplicator.Deduplicate(findings));

    /// <summary>Warnings describing the policy and instructions outcome, for the result.</summary>
    public static IEnumerable<string> Notes(PolicyOutcome outcome, InstructionsLoadResult? instructions)
    {
        if (instructions?.Warning is { } warning)
        {
            yield return warning;
        }

        if (outcome.ExcludedCount > 0)
        {
            yield return $"{outcome.ExcludedCount} finding(s) were not reported because of the review policy " +
                $"({string.Join(", ", outcome.Excluded.OrderBy(e => e.Key).Select(e => $"{Describe(e.Key)}: {e.Value}"))}).";
        }
    }

    private static string Describe(PolicyExclusion exclusion) => exclusion switch
    {
        PolicyExclusion.RuleNotEnabled => "rule not enabled",
        PolicyExclusion.Unclassified => "unclassified",
        PolicyExclusion.BelowMinimumSeverity => "below minimum severity",
        _ => "category not selected",
    };
}
