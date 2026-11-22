using System.Collections.Immutable;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace RibatIQ.Infrastructure.Roslyn;

/// <summary>
/// Syntax-based rules that stay accurate without project references or a successful build.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
internal sealed class RibatRulesAnalyzer : DiagnosticAnalyzer
{
    private const int MinSecretLength = 8;

    // Compared against the identifier lower-cased with '_' removed.
    private static readonly string[] SecretNameSuffixes =
        ["password", "passwd", "pwd", "secret", "apikey", "accesskey", "privatekey", "token"];

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
    [
        RibatRules.EmptyCatchBlock,
        RibatRules.AsyncVoid,
        RibatRules.RethrowLosesStackTrace,
        RibatRules.HardcodedSecret,
    ];

    public override void Initialize(AnalysisContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSyntaxNodeAction(AnalyzeCatchClause, SyntaxKind.CatchClause);
        context.RegisterSyntaxNodeAction(AnalyzeMethod, SyntaxKind.MethodDeclaration, SyntaxKind.LocalFunctionStatement);
        context.RegisterSyntaxNodeAction(AnalyzeThrow, SyntaxKind.ThrowStatement);
        context.RegisterSyntaxNodeAction(AnalyzeVariable, SyntaxKind.VariableDeclarator);
        context.RegisterSyntaxNodeAction(AnalyzeProperty, SyntaxKind.PropertyDeclaration);
        context.RegisterSyntaxNodeAction(AnalyzeAssignment, SyntaxKind.SimpleAssignmentExpression);
    }

    private static void AnalyzeCatchClause(SyntaxNodeAnalysisContext context)
    {
        var block = ((CatchClauseSyntax)context.Node).Block;

        // A comment inside the block documents a deliberate decision; respect it.
        var hasComment = block.DescendantTrivia().Any(t =>
            t.IsKind(SyntaxKind.SingleLineCommentTrivia) || t.IsKind(SyntaxKind.MultiLineCommentTrivia));

        if (block.Statements.Count == 0 && !hasComment)
        {
            context.ReportDiagnostic(Diagnostic.Create(RibatRules.EmptyCatchBlock, ((CatchClauseSyntax)context.Node).CatchKeyword.GetLocation()));
        }
    }

    private static void AnalyzeMethod(SyntaxNodeAnalysisContext context)
    {
        var (modifiers, returnType, identifier, parameters) = context.Node switch
        {
            MethodDeclarationSyntax m => (m.Modifiers, m.ReturnType, m.Identifier, m.ParameterList),
            LocalFunctionStatementSyntax f => (f.Modifiers, f.ReturnType, f.Identifier, f.ParameterList),
            _ => default,
        };

        if (returnType is PredefinedTypeSyntax { Keyword.RawKind: (int)SyntaxKind.VoidKeyword }
            && modifiers.Any(SyntaxKind.AsyncKeyword)
            && !LooksLikeEventHandler(parameters))
        {
            context.ReportDiagnostic(Diagnostic.Create(RibatRules.AsyncVoid, identifier.GetLocation(), identifier.ValueText));
        }
    }

    private static bool LooksLikeEventHandler(ParameterListSyntax parameters) =>
        parameters.Parameters.Count == 2
        && parameters.Parameters[1].Type?.ToString() is { } type
        && type.EndsWith("EventArgs", StringComparison.Ordinal);

    private static void AnalyzeThrow(SyntaxNodeAnalysisContext context)
    {
        var throwStatement = (ThrowStatementSyntax)context.Node;
        if (throwStatement.Expression is not IdentifierNameSyntax thrown)
        {
            return;
        }

        var catchClause = throwStatement.Ancestors().OfType<CatchClauseSyntax>().FirstOrDefault();
        var caught = catchClause?.Declaration?.Identifier.ValueText;

        if (!string.IsNullOrEmpty(caught) && thrown.Identifier.ValueText == caught)
        {
            context.ReportDiagnostic(Diagnostic.Create(RibatRules.RethrowLosesStackTrace, throwStatement.GetLocation(), caught));
        }
    }

    private static void AnalyzeVariable(SyntaxNodeAnalysisContext context)
    {
        var declarator = (VariableDeclaratorSyntax)context.Node;
        ReportIfSecret(context, declarator.Identifier, declarator.Initializer?.Value);
    }

    private static void AnalyzeProperty(SyntaxNodeAnalysisContext context)
    {
        var property = (PropertyDeclarationSyntax)context.Node;
        ReportIfSecret(context, property.Identifier, property.Initializer?.Value ?? property.ExpressionBody?.Expression);
    }

    private static void AnalyzeAssignment(SyntaxNodeAnalysisContext context)
    {
        var assignment = (AssignmentExpressionSyntax)context.Node;
        var target = assignment.Left switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier,
            MemberAccessExpressionSyntax member => member.Name.Identifier,
            _ => default,
        };

        if (target != default)
        {
            ReportIfSecret(context, target, assignment.Right);
        }
    }

    private static void ReportIfSecret(SyntaxNodeAnalysisContext context, SyntaxToken name, ExpressionSyntax? value)
    {
        if (value is not LiteralExpressionSyntax { RawKind: (int)SyntaxKind.StringLiteralExpression } literal)
        {
            return;
        }

        var text = literal.Token.ValueText;
        if (text.Length < MinSecretLength || text.Any(char.IsWhiteSpace) || !IsSecretName(name.ValueText))
        {
            return;
        }

        // The diagnostic names the identifier only; the literal value is never copied into findings.
        context.ReportDiagnostic(Diagnostic.Create(RibatRules.HardcodedSecret, name.GetLocation(), name.ValueText));
    }

    private static bool IsSecretName(string identifier)
    {
        var normalized = identifier.Replace("_", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
        return SecretNameSuffixes.Any(suffix => normalized.EndsWith(suffix.ToUpperInvariant(), StringComparison.Ordinal));
    }
}
