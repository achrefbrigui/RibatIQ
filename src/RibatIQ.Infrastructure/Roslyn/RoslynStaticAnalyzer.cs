using System.Collections.Immutable;
using System.Globalization;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

using RibatIQ.Application.Analysis;
using RibatIQ.Application.Rules;
using RibatIQ.Application.Sources;
using RibatIQ.Domain.Findings;
using RibatIQ.Domain.Rules;

namespace RibatIQ.Infrastructure.Roslyn;

/// <summary>
/// Runs Roslyn in-process over source documents: C# syntax errors plus the built-in RibatIQ analyzers.
/// </summary>
/// <remarks>
/// Documents are parsed into a reference-less compilation, so no project is built, restored or
/// executed and nothing is written to disk. Semantic diagnostics that need project references
/// (types, overloads, nullability) are therefore not reported.
/// </remarks>
public sealed class RoslynStaticAnalyzer : IStaticAnalyzer
{
    public const string CompilerSource = "roslyn";

    private static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.Latest, DocumentationMode.None);

    private readonly RuleCatalog _catalog;

    public RoslynStaticAnalyzer(RuleCatalog? catalog = null)
    {
        _catalog = catalog ?? RuleCatalog.Default;
    }

    /// <summary>Catalogue rules this analyzer can report.</summary>
    public static IReadOnlyList<ReviewRuleId> SupportedRules { get; } =
        [RibatRules.SyntaxError, .. RibatRules.CatalogueRules.Values];

    public string Coverage => "C# syntax errors and RibatIQ rules (RIQ1001-RIQ1004); no build or semantic analysis";

    public async Task<StaticAnalysisResult> AnalyzeAsync(IReadOnlyList<SourceDocument> documents, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(documents);

        var findings = new List<ReviewFinding>();
        var warnings = new List<string>();

        var trees = documents
            .Select(d => CSharpSyntaxTree.ParseText(d.Text, ParseOptions, d.Path, cancellationToken: cancellationToken))
            .ToArray();

        foreach (var tree in trees)
        {
            var syntaxRule = _catalog.Get(RibatRules.SyntaxError);
            findings.AddRange(tree.GetDiagnostics(cancellationToken)
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => ToFinding(d, syntaxRule, CompilerSource)));
        }

        if (trees.Length > 0)
        {
            var compilation = CSharpCompilation.Create(
                "RibatIQ.Analysis",
                trees,
                references: [],
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            var withAnalyzers = compilation.WithAnalyzers(
                ImmutableArray.Create<DiagnosticAnalyzer>(new RibatRulesAnalyzer()),
                new CompilationWithAnalyzersOptions(
                    options: new AnalyzerOptions([]),
                    onAnalyzerException: null,
                    concurrentAnalysis: true,
                    logAnalyzerExecutionTime: false));

            foreach (var diagnostic in await withAnalyzers.GetAnalyzerDiagnosticsAsync(cancellationToken).ConfigureAwait(false))
            {
                if (RibatRules.CatalogueRules.TryGetValue(diagnostic.Id, out var ruleId))
                {
                    findings.Add(ToFinding(diagnostic, _catalog.Get(ruleId), RibatRules.Source));
                }
                else
                {
                    // Analyzer failures (AD0001) must not become findings; surface them as warnings.
                    warnings.Add($"Static analysis issue {diagnostic.Id}: {diagnostic.GetMessage(CultureInfo.InvariantCulture)}");
                }
            }
        }

        return new StaticAnalysisResult(findings, warnings);
    }

    /// <summary>A deterministic finding classified by its catalogue rule (title, severity, category).</summary>
    private static ReviewFinding ToFinding(Diagnostic diagnostic, RuleDefinition rule, string source)
    {
        var (path, start, end) = GetLocation(diagnostic);
        return ReviewFinding.Create(
            title: rule.Title,
            description: diagnostic.GetMessage(CultureInfo.InvariantCulture),
            severity: rule.DefaultSeverity,
            category: rule.Category,
            filePath: path,
            startLine: start,
            endLine: end,
            suggestedFix: null,
            confidence: null,
            origin: FindingOrigin.StaticAnalysis,
            source: source,
            toolRuleId: diagnostic.Id,
            ruleId: rule.Id);
    }

    private static (string Path, int Start, int? End) GetLocation(Diagnostic diagnostic)
    {
        var span = diagnostic.Location.GetLineSpan();
        var start = span.StartLinePosition.Line + 1;
        var end = span.EndLinePosition.Line + 1;
        return (span.Path, start, end > start ? end : null);
    }
}
