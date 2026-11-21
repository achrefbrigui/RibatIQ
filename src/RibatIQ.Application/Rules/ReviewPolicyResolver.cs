using RibatIQ.Application.Configuration;
using RibatIQ.Domain.Findings;
using RibatIQ.Domain.Policies;
using RibatIQ.Domain.Rules;

namespace RibatIQ.Application.Rules;

/// <summary>
/// Builds the effective <see cref="ReviewPolicy"/> from the rule catalogue, a profile and explicit
/// enable/disable lists.
/// </summary>
/// <remarks>
/// Resolution order: rules enabled by default, plus the profile's <c>enable</c>, minus its <c>disable</c>,
/// restricted to the profile's categories when it has any; then the explicit enable list, then the
/// explicit disable list. Rules explicitly enabled outside the profile's categories keep their category
/// in the policy's category filter. Project instructions are carried along but never take part.
/// </remarks>
public static class ReviewPolicyResolver
{
    /// <exception cref="ConfigurationException">Unknown profile or rule, or a rule both enabled and disabled.</exception>
    public static ReviewPolicy Resolve(
        RuleCatalog catalog,
        string? profileName = null,
        IEnumerable<string>? enable = null,
        IEnumerable<string>? disable = null,
        FindingSeverity minimumSeverity = FindingSeverity.Info,
        bool includeUnclassified = true,
        ProjectInstructions? instructions = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        var name = string.IsNullOrWhiteSpace(profileName) ? RuleCatalog.DefaultProfileName : profileName.Trim();
        var profile = catalog.FindProfile(name)
            ?? throw new ConfigurationException(
                $"Unknown review profile '{name}'. Available profiles: {string.Join(", ", catalog.Profiles.Select(p => p.Name))}.");

        var explicitEnable = ParseRules(catalog, enable, "enable");
        var explicitDisable = ParseRules(catalog, disable, "disable");

        var conflict = explicitEnable.Intersect(explicitDisable).Select(r => r.Value).Order(StringComparer.Ordinal).FirstOrDefault();
        if (conflict is not null)
        {
            throw new ConfigurationException($"Rule '{conflict}' is both enabled and disabled.");
        }

        var enabled = catalog.Rules.Where(r => r.EnabledByDefault).Select(r => r.Id).ToHashSet();
        enabled.UnionWith(profile.Enable);
        enabled.ExceptWith(profile.Disable);

        if (profile.Categories is { } profileCategories)
        {
            enabled.RemoveWhere(id => !profileCategories.Contains(catalog.Get(id).Category));
        }

        enabled.UnionWith(explicitEnable);
        enabled.ExceptWith(explicitDisable);

        var disabled = profile.Disable.Concat(explicitDisable).Except(explicitEnable).ToHashSet();

        HashSet<FindingCategory>? categories = null;
        if (profile.Categories is not null)
        {
            categories = [.. profile.Categories, .. explicitEnable.Select(id => catalog.Get(id).Category)];
        }

        return new ReviewPolicy(profile.Name, enabled, disabled, minimumSeverity, categories, includeUnclassified, instructions);
    }

    private static HashSet<ReviewRuleId> ParseRules(RuleCatalog catalog, IEnumerable<string>? values, string kind)
    {
        var result = new HashSet<ReviewRuleId>();
        var unknown = new List<string>();

        foreach (var value in values ?? [])
        {
            if (ReviewRuleId.TryParse(value, out var id) && catalog.Find(id) is not null)
            {
                result.Add(id);
            }
            else
            {
                unknown.Add(value.Trim());
            }
        }

        return unknown.Count == 0
            ? result
            : throw new ConfigurationException(
                $"Cannot {kind} unknown rule(s): {string.Join(", ", unknown)}. Rule IDs look like SECURITY.SQL_INJECTION; see the rule catalogue.");
    }
}
