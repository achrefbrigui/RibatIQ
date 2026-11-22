using RibatIQ.Application.Sources;
using RibatIQ.Domain.Findings;
using RibatIQ.Domain.Rules;

namespace RibatIQ.Application.Analysis;

/// <summary>Deterministic analysis of source documents (compiler and analyzer diagnostics).</summary>
public interface IStaticAnalyzer
{
    /// <summary>Short description of what the analyzer covers, shown to users with the results.</summary>
    string Coverage { get; }

    /// <summary>Catalogue rules this analyzer can report; used to record which rules a run checked.</summary>
    IReadOnlyList<ReviewRuleId> SupportedRules => [];

    Task<StaticAnalysisResult> AnalyzeAsync(IReadOnlyList<SourceDocument> documents, CancellationToken cancellationToken);
}

/// <summary>Findings from static analysis, plus non-fatal issues met while analyzing.</summary>
public sealed record StaticAnalysisResult(IReadOnlyList<ReviewFinding> Findings, IReadOnlyList<string> Warnings);
