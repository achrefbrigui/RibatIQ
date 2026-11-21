using System.Text.Json;

using RibatIQ.Domain.Findings;

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
    public static AiParseResult Parse(string responseText, string source, AiContext context, AiOptions options)
    {
        ArgumentNullException.ThrowIfNull(responseText);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(options);

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
                if (TryMap(item, source, context) is { } finding)
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

    private static ReviewFinding? TryMap(JsonElement item, string source, AiContext context)
    {
        if (item.ValueKind != JsonValueKind.Object
            || GetString(item, "title") is not { } title
            || GetString(item, "description") is not { } description
            || ParseSeverity(GetString(item, "severity")) is not { } severity
            || ParseCategory(GetString(item, "category")) is not { } category
            || GetString(item, "file") is not { } file
            || GetInt(item, "startLine") is not { } startLine)
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
                toolRuleId: null);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

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
