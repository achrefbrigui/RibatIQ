using RibatIQ.Domain.Rules;

namespace RibatIQ.Domain.Results;

/// <summary>Whether a check actually ran.</summary>
public enum CheckStatus
{
    /// <summary>The check ran to completion. With no findings for its rules, it found no issue.</summary>
    Completed = 0,

    /// <summary>The check did not run (not requested, not applicable, or missing prerequisites).</summary>
    Skipped,

    /// <summary>The check started but could not complete.</summary>
    Failed,
}

/// <summary>
/// The outcome of one check (an analyzer, an audit or an AI review) in a run. A skipped or failed check
/// is never reported as a successful one.
/// </summary>
public sealed record CheckResult
{
    public const int MaxReasonLength = 500;

    public CheckResult(string checkId, string name, CheckStatus status, string? reason = null, IEnumerable<ReviewRuleId>? ruleIds = null)
    {
        CheckId = ValidateCheckId(checkId);

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Check name must not be empty.", nameof(name));
        }

        Status = Enum.IsDefined(status)
            ? status
            : throw new ArgumentOutOfRangeException(nameof(status), status, "Undefined check status.");

        var trimmedReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        if (Status != CheckStatus.Completed && trimmedReason is null)
        {
            throw new ArgumentException("A skipped or failed check must say why.", nameof(reason));
        }

        if (trimmedReason is { Length: > MaxReasonLength })
        {
            throw new ArgumentException($"Reason must not exceed {MaxReasonLength} characters.", nameof(reason));
        }

        Name = name.Trim();
        Reason = trimmedReason;
        RuleIds = (ruleIds ?? []).Distinct().OrderBy(r => r.Value, StringComparer.Ordinal).ToArray();
    }

    /// <summary>Stable identifier such as <c>roslyn.syntax</c> or <c>ai.review</c>.</summary>
    public string CheckId { get; }

    public string Name { get; }

    public CheckStatus Status { get; }

    /// <summary>Why the check was skipped or failed; optional detail when completed.</summary>
    public string? Reason { get; }

    /// <summary>Rules this check is responsible for.</summary>
    public IReadOnlyList<ReviewRuleId> RuleIds { get; }

    public static CheckResult Completed(string checkId, string name, IEnumerable<ReviewRuleId>? ruleIds = null) =>
        new(checkId, name, CheckStatus.Completed, reason: null, ruleIds);

    public static CheckResult Skipped(string checkId, string name, string reason, IEnumerable<ReviewRuleId>? ruleIds = null) =>
        new(checkId, name, CheckStatus.Skipped, reason, ruleIds);

    public static CheckResult Failed(string checkId, string name, string reason, IEnumerable<ReviewRuleId>? ruleIds = null) =>
        new(checkId, name, CheckStatus.Failed, reason, ruleIds);

    private static string ValidateCheckId(string checkId)
    {
        var trimmed = checkId?.Trim();
        if (string.IsNullOrEmpty(trimmed)
            || trimmed.Length > 64
            || !char.IsAsciiLetterLower(trimmed[0])
            || !trimmed.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c is '.' or '-'))
        {
            throw new ArgumentException("Check IDs use lower-case letters, digits, '.' and '-', starting with a letter.", nameof(checkId));
        }

        return trimmed;
    }
}
