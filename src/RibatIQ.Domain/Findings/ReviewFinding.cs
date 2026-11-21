using RibatIQ.Domain.Rules;

namespace RibatIQ.Domain.Findings;

/// <summary>
/// A single, validated issue reported against a file. Instances are always in a valid, normalized
/// state: construction fails with <see cref="ArgumentException"/> on invalid input, which makes this
/// type the trust boundary for findings coming from analyzers and, above all, from LLMs.
/// </summary>
public sealed record ReviewFinding
{
    public const int MaxTitleLength = 300;
    public const int MaxTextLength = 8_000;
    public const int MaxIdentifierLength = 128;

    /// <summary>
    /// Creates a finding from already-known values, for example when reading back a saved report.
    /// Use <see cref="Create"/> to have the stable <see cref="Id"/> computed.
    /// </summary>
    public ReviewFinding(
        string id,
        string title,
        string description,
        FindingSeverity severity,
        FindingCategory category,
        string filePath,
        int? startLine,
        int? endLine,
        string? suggestedFix,
        double? confidence,
        FindingOrigin origin,
        string source,
        string? toolRuleId,
        ReviewRuleId? ruleId = null,
        string? evidence = null)
    {
        if (!FindingFingerprint.IsWellFormed(id))
        {
            throw new ArgumentException(
                $"Finding ID must be a {FindingFingerprint.Length}-character lowercase hexadecimal fingerprint.",
                nameof(id));
        }

        Id = id;
        Title = RequireText(title, MaxTitleLength, nameof(title));
        Description = RequireText(description, MaxTextLength, nameof(description));
        Severity = RequireDefined(severity, nameof(severity));
        Category = RequireDefined(category, nameof(category));
        FilePath = RepositoryPath.Normalize(filePath, nameof(filePath));
        (StartLine, EndLine) = ValidateLines(startLine, endLine);
        SuggestedFix = OptionalText(suggestedFix, MaxTextLength, nameof(suggestedFix));
        Origin = RequireDefined(origin, nameof(origin));
        Confidence = ValidateConfidence(confidence, Origin);
        Source = RequireText(source, MaxIdentifierLength, nameof(source));
        ToolRuleId = OptionalText(toolRuleId, MaxIdentifierLength, nameof(toolRuleId));
        RuleId = ruleId;
        Evidence = OptionalText(evidence, MaxEvidenceLength, nameof(evidence));
    }

    public const int MaxEvidenceLength = 2_000;

    /// <summary>Stable fingerprint; see <see cref="FindingFingerprint"/>.</summary>
    public string Id { get; }

    public string Title { get; }

    public string Description { get; }

    public FindingSeverity Severity { get; }

    public FindingCategory Category { get; }

    /// <summary>Repository-relative path using forward slashes.</summary>
    public string FilePath { get; }

    /// <summary>1-based first line, or <see langword="null"/> for file-level findings.</summary>
    public int? StartLine { get; }

    /// <summary>1-based last line (inclusive), or <see langword="null"/> when not known.</summary>
    public int? EndLine { get; }

    /// <summary>Optional remediation hint. Never applied automatically.</summary>
    public string? SuggestedFix { get; }

    /// <summary>
    /// Heuristic score in [0, 1] for AI findings only. It is <b>not</b> a calibrated probability and must
    /// not be treated as authoritative. Always <see langword="null"/> for deterministic findings.
    /// </summary>
    public double? Confidence { get; }

    public FindingOrigin Origin { get; }

    /// <summary>Producer identifier, for example <c>roslyn</c> or an LLM provider ID.</summary>
    public string Source { get; }

    /// <summary>The producing tool's own diagnostic ID, such as <c>CS1026</c> or <c>RIQ1001</c>, when one exists.</summary>
    public string? ToolRuleId { get; }

    /// <summary>The RibatIQ review rule this finding belongs to, when it is classified.</summary>
    public ReviewRuleId? RuleId { get; }

    /// <summary>Short evidence or reasoning supporting the finding (never a copy of secret values).</summary>
    public string? Evidence { get; }

    /// <summary>Creates a finding and computes its stable <see cref="Id"/>.</summary>
    public static ReviewFinding Create(
        string title,
        string description,
        FindingSeverity severity,
        FindingCategory category,
        string filePath,
        int? startLine,
        int? endLine,
        string? suggestedFix,
        double? confidence,
        FindingOrigin origin,
        string source,
        string? toolRuleId,
        ReviewRuleId? ruleId = null,
        string? evidence = null)
    {
        var id = FindingFingerprint.Compute(filePath, ruleId, toolRuleId, title, startLine);
        return new ReviewFinding(
            id, title, description, severity, category, filePath, startLine, endLine,
            suggestedFix, confidence, origin, source, toolRuleId, ruleId, evidence);
    }

    private static string RequireText(string? value, int maxLength, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value must not be empty.", paramName);
        }

        return EnsureMaxLength(value.Trim(), maxLength, paramName);
    }

    private static string? OptionalText(string? value, int maxLength, string paramName) =>
        string.IsNullOrWhiteSpace(value) ? null : EnsureMaxLength(value.Trim(), maxLength, paramName);

    private static string EnsureMaxLength(string value, int maxLength, string paramName) =>
        value.Length <= maxLength
            ? value
            : throw new ArgumentException($"Value must not exceed {maxLength} characters.", paramName);

    private static TEnum RequireDefined<TEnum>(TEnum value, string paramName)
        where TEnum : struct, Enum =>
        Enum.IsDefined(value)
            ? value
            : throw new ArgumentOutOfRangeException(paramName, value, $"Undefined {typeof(TEnum).Name} value.");

    private static (int? Start, int? End) ValidateLines(int? startLine, int? endLine)
    {
        if (startLine is < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(startLine), startLine, "Start line must be 1 or greater.");
        }

        if (endLine is not null && startLine is null)
        {
            throw new ArgumentException("End line requires a start line.", nameof(endLine));
        }

        if (endLine < startLine)
        {
            throw new ArgumentOutOfRangeException(nameof(endLine), endLine, "End line must not precede start line.");
        }

        return (startLine, endLine);
    }

    private static double? ValidateConfidence(double? confidence, FindingOrigin origin)
    {
        if (confidence is not { } value)
        {
            return null;
        }

        if (origin == FindingOrigin.StaticAnalysis)
        {
            throw new ArgumentException("Deterministic findings must not carry a confidence score.", nameof(confidence));
        }

        if (!double.IsFinite(value) || value is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(confidence), confidence, "Confidence must be between 0 and 1.");
        }

        return confidence;
    }
}
