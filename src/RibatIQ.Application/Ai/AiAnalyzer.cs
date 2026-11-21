using System.Globalization;

using RibatIQ.Application.Reviewing;
using RibatIQ.Application.Sources;
using RibatIQ.Domain.Findings;

namespace RibatIQ.Application.Ai;

/// <summary>Result of AI analysis, with what was sent and what it cost.</summary>
/// <param name="Findings">Validated AI findings.</param>
/// <param name="Warnings">Budget, validation and truncation notices.</param>
/// <param name="ProviderId">Provider that served the request.</param>
/// <param name="Model">Model that served the request.</param>
/// <param name="FilesSent">Number of files (or review excerpts) sent to the model.</param>
/// <param name="Usage">Token usage, when reported.</param>
public sealed record AiAnalysisResult(
    IReadOnlyList<ReviewFinding> Findings,
    IReadOnlyList<string> Warnings,
    string ProviderId,
    string Model,
    int FilesSent,
    LlmUsage? Usage);

/// <summary>
/// Runs one AI analysis request: bounded context, prompt, provider call, strict validation.
/// Provider failures and malformed responses become <see cref="OperationFailedException"/> so a failed
/// AI analysis is never reported as a clean result.
/// </summary>
public sealed class AiAnalyzer
{
    private readonly AiOptions _options;
    private readonly AiPromptBuilder _prompts;

    public AiAnalyzer(AiOptions? options = null, AiPromptBuilder? prompts = null)
    {
        _options = options ?? AiOptions.Default;
        _prompts = prompts ?? new AiPromptBuilder();
    }

    public Task<AiAnalysisResult> AnalyzeScanAsync(
        ILlmProvider provider, IReadOnlyList<SourceDocument> documents, CancellationToken cancellationToken)
    {
        var context = AiContextBuilder.ForScan(documents, _options);
        return RunAsync(provider, context, _prompts.BuildScan(context, _options), cancellationToken);
    }

    public Task<AiAnalysisResult> AnalyzeReviewAsync(
        ILlmProvider provider,
        IReadOnlyList<SourceDocument> documents,
        IReadOnlyList<FileChange> changes,
        string range,
        CancellationToken cancellationToken)
    {
        var context = AiContextBuilder.ForReview(documents, changes, _options);
        return RunAsync(provider, context, _prompts.BuildReview(context, range, _options), cancellationToken);
    }

    private async Task<AiAnalysisResult> RunAsync(
        ILlmProvider provider, AiContext context, LlmRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(provider);

        var warnings = new List<string>(context.Warnings);
        if (context.Files.Count == 0)
        {
            warnings.Add("AI analysis: no source was sent to the model (nothing to analyze within the limits).");
            return new AiAnalysisResult([], warnings, provider.ProviderId, provider.Model, 0, null);
        }

        LlmResponse response;
        AiParseResult parsed;
        try
        {
            response = await provider.GenerateAsync(request, cancellationToken).ConfigureAwait(false);
            parsed = AiFindingParser.Parse(response.Text, provider.ProviderId, context, _options);
        }
        catch (LlmProviderException ex)
        {
            throw new OperationFailedException($"AI analysis with {provider.ProviderId} ({provider.Model}) failed: {ex.Message}", ex);
        }

        if (parsed.Rejected > 0)
        {
            warnings.Add(string.Create(CultureInfo.InvariantCulture,
                $"AI analysis: {parsed.Rejected} finding(s) were ignored because they were malformed or did not point to the code that was sent."));
        }

        if (parsed.Truncated > 0)
        {
            warnings.Add(string.Create(CultureInfo.InvariantCulture,
                $"AI analysis: {parsed.Truncated} finding(s) beyond the limit of {_options.MaxFindings} were ignored."));
        }

        return new AiAnalysisResult(parsed.Findings, warnings, provider.ProviderId, response.Model, context.Files.Count, response.Usage);
    }
}
