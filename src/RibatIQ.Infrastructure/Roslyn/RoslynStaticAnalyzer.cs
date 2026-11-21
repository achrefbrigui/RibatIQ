using System.Collections.Immutable;
using System.Globalization;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

using RibatIQ.Application.Analysis;
using RibatIQ.Application.Sources;
using RibatIQ.Domain.Findings;

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
            findings.AddRange(tree.GetDiagnostics(cancellationToken)
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(ToSyntaxErrorFinding));
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
                if (RibatRules.Classification.TryGetValue(diagnostic.Id, out var classification))
                {
                    findings.Add(ToRuleFinding(diagnostic, classification.Severity, classification.Category));
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

    private static ReviewFinding ToSyntaxErrorFinding(Diagnostic diagnostic)
    {
        var (path, start, end) = GetLocation(diagnostic);
        return ReviewFinding.Create(
            title: "C# syntax error",
            description: diagnostic.GetMessage(CultureInfo.InvariantCulture),
            severity: FindingSeverity.High,
            category: FindingCategory.Correctness,
            filePath: path,
            startLine: start,
            endLine: end,
            suggestedFix: null,
            confidence: null,
            origin: FindingOrigin.StaticAnalysis,
            source: CompilerSource,
            toolRuleId: diagnostic.Id);
    }

    private static ReviewFinding ToRuleFinding(Diagnostic diagnostic, FindingSeverity severity, FindingCategory category)
    {
        var (path, start, end) = GetLocation(diagnostic);
        return ReviewFinding.Create(
            title: diagnostic.Descriptor.Title.ToString(CultureInfo.InvariantCulture),
            description: diagnostic.GetMessage(CultureInfo.InvariantCulture),
            severity: severity,
            category: category,
            filePath: path,
            startLine: start,
            endLine: end,
            suggestedFix: null,
            confidence: null,
            origin: FindingOrigin.StaticAnalysis,
            source: RibatRules.Source,
            toolRuleId: diagnostic.Id);
    }

    private static (string Path, int Start, int? End) GetLocation(Diagnostic diagnostic)
    {
        var span = diagnostic.Location.GetLineSpan();
        var start = span.StartLinePosition.Line + 1;
        var end = span.EndLinePosition.Line + 1;
        return (span.Path, start, end > start ? end : null);
    }
}
