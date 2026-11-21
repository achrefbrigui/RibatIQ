namespace RibatIQ.Application.Ai;

/// <summary>Limits applied to AI analysis, so that no run sends the whole repository or trusts unbounded output.</summary>
/// <param name="MaxPromptChars">Total characters of source context sent in one request.</param>
/// <param name="MaxFileChars">Largest single file (scan) or excerpt (review) sent.</param>
/// <param name="ReviewContextLines">Unchanged lines included above and below each change in a review.</param>
/// <param name="MaxFindings">Most AI findings accepted from one response.</param>
/// <param name="MaxOutputTokens">Output token limit requested from the provider.</param>
public sealed record AiOptions(
    int MaxPromptChars = 100_000,
    int MaxFileChars = 40_000,
    int ReviewContextLines = 20,
    int MaxFindings = 50,
    int MaxOutputTokens = 16_000)
{
    public static AiOptions Default { get; } = new();
}
