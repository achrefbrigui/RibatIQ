using System.Text.Json;
using System.Text.Json.Serialization;

using RibatIQ.Domain.Findings;
using RibatIQ.Domain.Rules;

namespace RibatIQ.Application.Rules;

/// <summary>
/// The rules RibatIQ supports and the built-in profiles, loaded from <c>rules/catalog.json</c> (embedded in
/// this assembly). The JSON only describes rules: every entry is validated and converted to domain types,
/// and the whole catalogue is rejected if anything is inconsistent.
/// </summary>
public sealed class RuleCatalog
{
    public const int SupportedVersion = 1;
    public const string DefaultProfileName = "default";
    public const string ResourceName = "RibatIQ.Rules.catalog.json";

    private static readonly Lazy<RuleCatalog> Embedded = new(LoadEmbedded);

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    private readonly Dictionary<ReviewRuleId, RuleDefinition> _rules;
    private readonly Dictionary<string, RuleProfile> _profiles;

    private RuleCatalog(IReadOnlyList<RuleDefinition> rules, IReadOnlyList<RuleProfile> profiles)
    {
        Rules = rules;
        Profiles = profiles;
        _rules = rules.ToDictionary(r => r.Id);
        _profiles = profiles.ToDictionary(p => p.Name, StringComparer.Ordinal);
    }

    /// <summary>The catalogue shipped with RibatIQ.</summary>
    public static RuleCatalog Default => Embedded.Value;

    /// <summary>Rules in catalogue order.</summary>
    public IReadOnlyList<RuleDefinition> Rules { get; }

    /// <summary>Profiles in catalogue order; always contains <see cref="DefaultProfileName"/>.</summary>
    public IReadOnlyList<RuleProfile> Profiles { get; }

    public RuleDefinition? Find(ReviewRuleId id)
    {
        ArgumentNullException.ThrowIfNull(id);
        return _rules.GetValueOrDefault(id);
    }

    /// <exception cref="KeyNotFoundException">The rule is not in the catalogue.</exception>
    public RuleDefinition Get(ReviewRuleId id) =>
        Find(id) ?? throw new KeyNotFoundException($"Rule '{id}' is not in the rule catalogue.");

    public RuleProfile? FindProfile(string name) => _profiles.GetValueOrDefault(name?.Trim() ?? string.Empty);

    /// <summary>Rules detected by <paramref name="detection"/>, in catalogue order.</summary>
    public IReadOnlyList<RuleDefinition> RulesDetectedBy(DetectionMethod detection) =>
        Rules.Where(r => r.Detection == detection).ToArray();

    /// <summary>Parses and validates a catalogue document.</summary>
    /// <exception cref="RuleCatalogException">The document is malformed or inconsistent; the message lists every problem.</exception>
    public static RuleCatalog Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        CatalogDocument? document;
        try
        {
            document = JsonSerializer.Deserialize<CatalogDocument>(json, SerializerOptions);
        }
        catch (JsonException ex)
        {
            throw new RuleCatalogException($"The rule catalogue is not valid JSON or has unknown fields: {ex.Message}", ex);
        }

        return Validate(document ?? throw new RuleCatalogException("The rule catalogue is empty."));
    }

    internal static string CategoryName(FindingCategory category) => JsonNamingPolicy.CamelCase.ConvertName(category.ToString());

    internal static string SeverityName(FindingSeverity severity) => severity.ToString().ToLowerInvariant();

    internal static string DetectionName(DetectionMethod detection) => detection switch
    {
        DetectionMethod.StaticAnalysis => "static-analysis",
        DetectionMethod.DependencyAudit => "dependency-audit",
        DetectionMethod.AiReview => "ai-review",
        DetectionMethod.TestAnalysis => "test-analysis",
        DetectionMethod.CoverageAnalysis => "coverage-analysis",
        DetectionMethod.ConfigurationAnalysis => "configuration-analysis",
        _ => throw new ArgumentOutOfRangeException(nameof(detection), detection, "Undefined detection method."),
    };

    private static RuleCatalog LoadEmbedded()
    {
        using var stream = typeof(RuleCatalog).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new RuleCatalogException($"The embedded rule catalogue '{ResourceName}' is missing.");
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }

    private static RuleCatalog Validate(CatalogDocument document)
    {
        var errors = new List<string>();

        if (document.Version != SupportedVersion)
        {
            errors.Add($"version must be {SupportedVersion}.");
        }

        var rules = new List<RuleDefinition>();
        var seen = new HashSet<ReviewRuleId>();

        foreach (var (entry, index) in (document.Rules ?? []).Select((r, i) => (r, i)))
        {
            var where = $"rules[{index}]";
            if (entry is null)
            {
                errors.Add($"{where} is null.");
                continue;
            }

            if (!ReviewRuleId.TryParse(entry.Id, out var id))
            {
                errors.Add($"{where}: '{entry.Id}' is not a valid rule ID.");
                continue;
            }

            where = $"rule {id}";
            if (!seen.Add(id))
            {
                errors.Add($"{where} is defined more than once.");
            }

            var category = ParseName(entry.Category, Enum.GetValues<FindingCategory>(), CategoryName);
            var severity = ParseName(entry.DefaultSeverity, Enum.GetValues<FindingSeverity>(), SeverityName);
            var detection = ParseName(entry.Detection, Enum.GetValues<DetectionMethod>(), DetectionName);

            Require(category is not null, $"{where}: unknown category '{entry.Category}'.");
            Require(severity is not null, $"{where}: unknown default severity '{entry.DefaultSeverity}'.");
            Require(detection is not null, $"{where}: unknown detection '{entry.Detection}'.");
            Require(entry.EnabledByDefault is not null, $"{where}: enabledByDefault is required.");
            Require(IsText(entry.Title, 120), $"{where}: title is required (at most 120 characters).");
            Require(IsText(entry.Description, 500), $"{where}: description is required (at most 500 characters).");
            Require(
                !(detection == DetectionMethod.AiReview && severity == FindingSeverity.Critical),
                $"{where}: AI-review rules cannot default to critical.");
            Require(
                !(detection == DetectionMethod.AiReview && category == FindingCategory.Dependencies),
                $"{where}: dependency rules must be detected by tools, not by AI review.");

            if (category is { } c && severity is { } s && detection is { } d && entry.EnabledByDefault is { } enabled
                && IsText(entry.Title, 120) && IsText(entry.Description, 500))
            {
                rules.Add(new RuleDefinition(id, entry.Title!.Trim(), c, s, enabled, d, entry.Description!.Trim()));
            }
        }

        Require(rules.Count > 0 || errors.Count > 0, "the catalogue defines no rules.");

        var profiles = new List<RuleProfile>();
        var profileNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (entry, index) in (document.Profiles ?? []).Select((p, i) => (p, i)))
        {
            var name = entry?.Name?.Trim();
            var where = $"profile '{name ?? $"#{index}"}'";

            if (entry is null || string.IsNullOrEmpty(name) || !name.All(ch => char.IsAsciiLetterLower(ch) || char.IsAsciiDigit(ch) || ch == '-'))
            {
                errors.Add($"{where}: name must use lower-case letters, digits and '-'.");
                continue;
            }

            Require(profileNames.Add(name), $"{where} is defined more than once.");
            Require(IsText(entry.Description, 200), $"{where}: description is required.");

            var enable = ParseRuleList(entry.Enable, seen, $"{where} enable", errors);
            var disable = ParseRuleList(entry.Disable, seen, $"{where} disable", errors);
            Require(!enable.Intersect(disable).Any(), $"{where}: a rule is both enabled and disabled.");

            IReadOnlySet<FindingCategory>? categories = null;
            if (entry.Categories is not null)
            {
                var parsed = entry.Categories.Select(n => ParseName(n, Enum.GetValues<FindingCategory>(), CategoryName)).ToArray();
                Require(parsed.Length > 0, $"{where}: categories must not be empty when present.");
                Require(parsed.All(p => p is not null), $"{where}: unknown category in categories.");
                categories = parsed.OfType<FindingCategory>().ToHashSet();
            }

            profiles.Add(new RuleProfile(name, entry.Description?.Trim() ?? string.Empty, categories, enable, disable));
        }

        Require(profileNames.Contains(DefaultProfileName), $"the '{DefaultProfileName}' profile is required.");

        if (errors.Count > 0)
        {
            throw new RuleCatalogException("The rule catalogue is invalid:" + Environment.NewLine + "- " + string.Join(Environment.NewLine + "- ", errors));
        }

        return new RuleCatalog(rules, profiles);

        void Require(bool condition, string error)
        {
            if (!condition)
            {
                errors.Add(error);
            }
        }
    }

    private static List<ReviewRuleId> ParseRuleList(string[]? values, HashSet<ReviewRuleId> known, string where, List<string> errors)
    {
        var result = new List<ReviewRuleId>();
        foreach (var value in values ?? [])
        {
            if (!ReviewRuleId.TryParse(value, out var id) || !known.Contains(id))
            {
                errors.Add($"{where}: '{value}' is not a rule in the catalogue.");
                continue;
            }

            result.Add(id);
        }

        return result;
    }

    private static T? ParseName<T>(string? value, IEnumerable<T> candidates, Func<T, string> name)
        where T : struct, Enum =>
        candidates.Cast<T?>().FirstOrDefault(c => string.Equals(name(c!.Value), value, StringComparison.Ordinal));

    private static bool IsText(string? value, int maxLength) =>
        !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= maxLength;

    private sealed record CatalogDocument(int? Version, RuleEntry?[]? Rules, ProfileEntry?[]? Profiles);

    private sealed record RuleEntry(
        string? Id,
        string? Title,
        string? Category,
        string? DefaultSeverity,
        bool? EnabledByDefault,
        string? Detection,
        string? Description);

    private sealed record ProfileEntry(string? Name, string? Description, string[]? Categories, string[]? Enable, string[]? Disable);
}
