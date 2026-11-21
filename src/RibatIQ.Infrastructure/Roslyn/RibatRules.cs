using Microsoft.CodeAnalysis;

using RibatIQ.Domain.Findings;

namespace RibatIQ.Infrastructure.Roslyn;

/// <summary>Built-in RibatIQ rules: Roslyn descriptors plus how their diagnostics map to findings.</summary>
internal static class RibatRules
{
    public const string Source = "ribatiq-rules";

    public static readonly DiagnosticDescriptor EmptyCatchBlock = Create(
        "RIQ1001",
        "Empty catch block",
        "The catch block is empty, so the exception is silently swallowed. Handle it, log it, or document why it is safe to ignore.");

    public static readonly DiagnosticDescriptor AsyncVoid = Create(
        "RIQ1002",
        "Async void method",
        "'{0}' is async void: callers cannot await it and its exceptions crash the process. Return Task instead, unless it is an event handler.");

    public static readonly DiagnosticDescriptor RethrowLosesStackTrace = Create(
        "RIQ1003",
        "Rethrow loses the stack trace",
        "'throw {0};' resets the exception's stack trace. Use 'throw;' to rethrow the caught exception.");

    public static readonly DiagnosticDescriptor HardcodedSecret = Create(
        "RIQ1004",
        "Possible hard-coded secret",
        "'{0}' is assigned a string literal that looks like a secret. Load secrets from configuration or a secret store.");

    /// <summary>Severity and category of each rule's findings.</summary>
    public static IReadOnlyDictionary<string, (FindingSeverity Severity, FindingCategory Category)> Classification { get; } =
        new Dictionary<string, (FindingSeverity, FindingCategory)>(StringComparer.Ordinal)
        {
            [EmptyCatchBlock.Id] = (FindingSeverity.Medium, FindingCategory.Correctness),
            [AsyncVoid.Id] = (FindingSeverity.Medium, FindingCategory.BestPractices),
            [RethrowLosesStackTrace.Id] = (FindingSeverity.Low, FindingCategory.Correctness),
            [HardcodedSecret.Id] = (FindingSeverity.High, FindingCategory.Security),
        };

    private static DiagnosticDescriptor Create(string id, string title, string messageFormat) =>
        new(id, title, messageFormat, "RibatIQ", DiagnosticSeverity.Warning, isEnabledByDefault: true);
}
