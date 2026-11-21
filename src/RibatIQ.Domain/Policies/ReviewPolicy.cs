using System.Collections.Frozen;

using RibatIQ.Domain.Findings;
using RibatIQ.Domain.Rules;

namespace RibatIQ.Domain.Policies;

/// <summary>Why a finding was removed by a <see cref="ReviewPolicy"/>.</summary>
public enum PolicyExclusion
{
    /// <summary>The finding's rule is not enabled.</summary>
    RuleNotEnabled = 0,

    /// <summary>The finding has no rule and unclassified findings are excluded.</summary>
    Unclassified,

    /// <summary>The finding is below the minimum severity.</summary>
    BelowMinimumSeverity,

    /// <summary>The finding's category is filtered out.</summary>
    CategoryExcluded,
}

/// <summary>Findings kept by a policy, and how many were removed for each reason.</summary>
public sealed record PolicyOutcome(IReadOnlyList<ReviewFinding> Kept, IReadOnlyDictionary<PolicyExclusion, int> Excluded)
{
    public int ExcludedCount => Excluded.Values.Sum();
}

/// <summary>
/// The effective review policy: which rules run and which findings are reported. It is resolved before a
/// review (from the rule catalogue, a profile and explicit settings) and evaluated independently of the
/// analyzers that produce findings.
/// </summary>
/// <remarks>
/// Project instructions are carried for reviewers but never change the policy itself.
/// </remarks>
public sealed record ReviewPolicy
{
    public const int MaxProfileNameLength = 40;

    public ReviewPolicy(
        string profile,
        IEnumerable<ReviewRuleId> enabledRules,
        IEnumerable<ReviewRuleId>? disabledRules = null,
        FindingSeverity minimumSeverity = FindingSeverity.Info,
        IEnumerable<FindingCategory>? categories = null,
        bool includeUnclassified = true,
        ProjectInstructions? instructions = null)
    {
        ArgumentNullException.ThrowIfNull(enabledRules);

        Profile = ValidateProfile(profile);
        EnabledRules = enabledRules.ToFrozenSet();
        DisabledRules = (disabledRules ?? []).ToFrozenSet();

        var conflict = EnabledRules.Intersect(DisabledRules).Select(r => r.Value).Order(StringComparer.Ordinal).FirstOrDefault();
        if (conflict is not null)
        {
            throw new ArgumentException($"Rule '{conflict}' cannot be both enabled and disabled.", nameof(disabledRules));
        }

        MinimumSeverity = Enum.IsDefined(minimumSeverity)
            ? minimumSeverity
            : throw new ArgumentOutOfRangeException(nameof(minimumSeverity), minimumSeverity, "Undefined severity.");

        if (categories is not null)
        {
            var set = categories.ToFrozenSet();
            if (set.Count == 0)
            {
                throw new ArgumentException("A category filter must contain at least one category; pass null for all categories.", nameof(categories));
            }

            foreach (var category in set)
            {
                if (!Enum.IsDefined(category))
                {
                    throw new ArgumentOutOfRangeException(nameof(categories), category, "Undefined category.");
                }
            }

            Categories = set;
        }

        IncludeUnclassified = includeUnclassified;
        Instructions = instructions;
    }

    /// <summary>Name of the profile the policy was built from, for example <c>default</c>.</summary>
    public string Profile { get; }

    /// <summary>Rules whose findings are produced and reported.</summary>
    public IReadOnlySet<ReviewRuleId> EnabledRules { get; }

    /// <summary>Rules explicitly disabled, recorded for traceability in reports.</summary>
    public IReadOnlySet<ReviewRuleId> DisabledRules { get; }

    /// <summary>Findings below this severity are not reported.</summary>
    public FindingSeverity MinimumSeverity { get; }

    /// <summary>Reported categories, or <see langword="null"/> for all.</summary>
    public IReadOnlySet<FindingCategory>? Categories { get; }

    /// <summary>Whether findings without a rule ID (for example unclassified AI findings) are reported.</summary>
    public bool IncludeUnclassified { get; }

    /// <summary>Project-specific guidance for reviewers; untrusted and informational only.</summary>
    public ProjectInstructions? Instructions { get; }

    public bool IsEnabled(ReviewRuleId ruleId)
    {
        ArgumentNullException.ThrowIfNull(ruleId);
        return EnabledRules.Contains(ruleId);
    }

    /// <summary>Returns why <paramref name="finding"/> is excluded, or <see langword="null"/> when it is reported.</summary>
    public PolicyExclusion? Evaluate(ReviewFinding finding)
    {
        ArgumentNullException.ThrowIfNull(finding);

        if (finding.RuleId is { } ruleId ? !IsEnabled(ruleId) : !IncludeUnclassified)
        {
            return finding.RuleId is null ? PolicyExclusion.Unclassified : PolicyExclusion.RuleNotEnabled;
        }

        if (finding.Severity < MinimumSeverity)
        {
            return PolicyExclusion.BelowMinimumSeverity;
        }

        if (Categories is not null && !Categories.Contains(finding.Category))
        {
            return PolicyExclusion.CategoryExcluded;
        }

        return null;
    }

    /// <summary>Keeps the findings the policy reports, preserving their order.</summary>
    public PolicyOutcome Apply(IEnumerable<ReviewFinding> findings)
    {
        ArgumentNullException.ThrowIfNull(findings);

        var kept = new List<ReviewFinding>();
        var excluded = new Dictionary<PolicyExclusion, int>();

        foreach (var finding in findings)
        {
            if (Evaluate(finding) is { } reason)
            {
                excluded[reason] = excluded.GetValueOrDefault(reason) + 1;
            }
            else
            {
                kept.Add(finding);
            }
        }

        return new PolicyOutcome(kept, excluded);
    }

    private static string ValidateProfile(string profile)
    {
        var trimmed = profile?.Trim();
        if (string.IsNullOrEmpty(trimmed)
            || trimmed.Length > MaxProfileNameLength
            || !char.IsAsciiLetterLower(trimmed[0])
            || !trimmed.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '-'))
        {
            throw new ArgumentException("Profile names use lower-case letters, digits and '-', starting with a letter.", nameof(profile));
        }

        return trimmed;
    }
}
