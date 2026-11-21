using System.Text.Json;

using RibatIQ.Application.Rules;
using RibatIQ.Domain.Findings;
using RibatIQ.Domain.Rules;

namespace RibatIQ.Application.Ai;

/// <summary>Outcome of parsing a model response.</summary>
/// <param name="Findings">Findings that passed validation.</param>
/// <param name="Rejected">Entries dropped because they failed validation.</param>
/// <param name="Truncated">Valid findings dropped because <see cref="AiOptions.MaxFindings"/> was exceeded.</param>
public sealed record AiParseResult(IReadOnlyList<ReviewFinding> Findings, int Rejected, int Truncated);

/// <summary>
/// Turns untrusted model output into findings. The response must be a JSON object with a
/// <c>findings</c> array; each entry is validated on its own and dropped when it is malformed or not
/// attributable to code the model was shown.
/// </summary>
/// <remarks>
/// An entry is kept only when its file was sent to the model, its line range lies inside the lines
/// shown, and, for reviews, the range touches a changed line. The model's severity is never above
/// <see cref="FindingSeverity.High"/>: critical findings are reserved for deterministic analysis.
/// Its confidence is kept only when it is a number between 0 and 1.
/// </remarks>
public static class AiFindingParser
{
    public const FindingSeverity MaxAiSeverity = FindingSeverity.High;

    private static readonly JsonDocumentOptions DocumentOptions = new() { MaxDepth = 16 };

    /// <exception cref="LlmProviderException">The response is not a JSON object with a <c>findings</c> array.</exception>
    /// <param name="responseText">Raw model output.</param>
    /// <param name="source">Provider ID recorded on each finding.</param>
    /// <param name="context">What the model was shown.</param>
    /// <param name="options">Limits.</param>
    /// <param name="rules">
    /// Rules the model was asked to apply. A finding's <c>ruleId</c> must be one of them or
    /// <see cref="AiPromptBuilder.UnclassifiedRuleId"/> (or absent); any other value rejects the finding.
    /// </param>
    public static AiParseResult Parse(
        string responseText, string source, AiContext context, AiOptions options, IReadOnlyList<RuleDefinition>? rules = null)
    {
        ArgumentNullException.ThrowIfNull(responseText);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(options);

        var allowed = (rules ?? []).ToDictionary(r => r.Id);

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(StripCodeFence(responseText), DocumentOptions);
        }
        catch (JsonException ex)
        {
            throw new LlmProviderException(LlmErrorKind.MalformedResponse, "The AI response is not valid JSON.", ex);
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("findings", out var items)
                || items.ValueKind != JsonValueKind.Array)
            {
                throw new LlmProviderException(LlmErrorKind.MalformedResponse, "The AI response has no 'findings' array.");
            }

            var findings = new List<ReviewFinding>();
            var rejected = 0;

            foreach (var item in items.EnumerateArray())
            {
                if (TryMap(item, source, context, allowed) is { } finding)
                {
                    findings.Add(finding);
                }
                else
                {
                    rejected++;
                }
            }

            var truncated = Math.Max(0, findings.Count - options.MaxFindings);
            return new AiParseResult(findings.Take(options.MaxFindings).ToArray(), rejected, truncated);
        }
    }

    private static ReviewFinding? TryMap(JsonElement item, string source, AiContext context, Dictionary<ReviewRuleId, RuleDefinition> allowed)
    {
        if (item.ValueKind != JsonValueKind.Object
            || GetString(item, "title") is not { } title
            || GetString(item, "description") is not { } description
            || ParseSeverity(GetString(item, "severity")) is not { } severity
            || GetString(item, "file") is not { } file
            || GetInt(item, "startLine") is not { } startLine
            || !TryGetRule(item, allowed, out var rule))
        {
            return null;
        }

        // A classified finding takes its category from the catalogue; only unclassified ones use the model's.
        if ((rule?.Category ?? ParseCategory(GetString(item, "category"))) is not { } category)
        {
            return null;
        }

        int? endLine = null;
        if (item.TryGetProperty("endLine", out var endElement) && endElement.ValueKind != JsonValueKind.Null)
        {
            if (GetInt(item, "endLine") is not { } end)
            {
                return null;
            }

            endLine = end == startLine ? null : end;
        }

        string path;
        try
        {
            path = RepositoryPath.Normalize(file);
        }
        catch (ArgumentException)
        {
            return null;
        }

        var excerpt = context.Find(path);
        var lastLine = endLine ?? startLine;
        if (excerpt is null || startLine < 1 || lastLine < startLine || !excerpt.Shows(startLine, lastLine))
        {
            return null;
        }

        if (excerpt.ChangedLines.Count > 0 && !excerpt.ChangedLines.Any(c => c.Overlaps(startLine, lastLine)))
        {
            return null;
        }

        try
        {
            return ReviewFinding.Create(
                title,
                description,
                severity > MaxAiSeverity ? MaxAiSeverity : severity,
                category,
                path,
                startLine,
                endLine,
                GetString(item, "suggestedFix"),
                GetConfidence(item),
                FindingOrigin.Ai,
                source,
                toolRuleId: null,
                ruleId: rule?.Id,
                evidence: Truncate(GetString(item, "evidence"), ReviewFinding.MaxEvidenceLength));
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// Reads <c>ruleId</c>: absent, null or <see cref="AiPromptBuilder.UnclassifiedRuleId"/> means unclassified;
    /// otherwise it must be one of the rules the model was given.
    /// </summary>
    private static bool TryGetRule(JsonElement item, Dictionary<ReviewRuleId, RuleDefinition> allowed, out RuleDefinition? rule)
    {
        rule = null;
        if (!item.TryGetProperty("ruleId", out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (value.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var text = value.GetString();
        if (string.Equals(text, AiPromptBuilder.UnclassifiedRuleId, StringComparison.Ordinal))
        {
            return true;
        }

        if (ReviewRuleId.TryParse(text, out var id) && allowed.TryGetValue(id, out var definition))
        {
            rule = definition;
            return true;
        }

        return false;
    }

    private static string? Truncate(string? text, int maxLength) =>
        text is { Length: > 0 } && text.Trim().Length > maxLength ? text.Trim()[..maxLength] : text;

    private static string StripCodeFence(string text)
    {
        var trimmed = text.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            return trimmed;
        }

        var firstNewline = trimmed.IndexOf('\n', StringComparison.Ordinal);
        var lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
        return firstNewline > 0 && lastFence > firstNewline ? trimmed[(firstNewline + 1)..lastFence].Trim() : trimmed;
    }

    private static string? GetString(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static int? GetInt(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
            ? number
            : null;

    private static double? GetConfidence(JsonElement item) =>
        item.TryGetProperty("confidence", out var value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetDouble(out var confidence)
        && double.IsFinite(confidence)
        && confidence is >= 0 and <= 1
            ? confidence
            : null;

    private static FindingSeverity? ParseSeverity(string? value) => value?.Trim().ToUpperInvariant() switch
    {
        "INFO" => FindingSeverity.Info,
        "LOW" => FindingSeverity.Low,
        "MEDIUM" => FindingSeverity.Medium,
        "HIGH" => FindingSeverity.High,
        "CRITICAL" => FindingSeverity.Critical,
        _ => null,
    };

    private static FindingCategory? ParseCategory(string? value) => value?.Trim().ToUpperInvariant() switch
    {
        "CORRECTNESS" => FindingCategory.Correctness,
        "SECURITY" => FindingCategory.Security,
        "PERFORMANCE" => FindingCategory.Performance,
        "BESTPRACTICES" => FindingCategory.BestPractices,
        "CLEANCODE" => FindingCategory.CleanCode,
        "ARCHITECTURE" => FindingCategory.Architecture,
        "TESTING" => FindingCategory.Testing,
        "PRODUCTIONREADINESS" => FindingCategory.ProductionReadiness,
        _ => null,
    };
}
