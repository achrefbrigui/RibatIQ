namespace RibatIQ.Application.Reviewing;

/// <summary>Input of the review workflow: the Git revisions whose difference is reviewed.</summary>
public sealed record ReviewRequest
{
    public const string DefaultHeadRef = "HEAD";
    public const int MaxRefLength = 256;

    public ReviewRequest(string baseRef, string headRef)
    {
        BaseRef = ValidateRef(baseRef, nameof(baseRef));
        HeadRef = ValidateRef(headRef, nameof(headRef));
    }

    /// <summary>Revision the changes are compared against, for example <c>origin/main</c>.</summary>
    public string BaseRef { get; }

    /// <summary>Revision containing the changes, for example <c>HEAD</c>.</summary>
    public string HeadRef { get; }

    /// <summary>Human-readable range, used as the analysis target.</summary>
    public string Range => $"{BaseRef}...{HeadRef}";

    /// <summary>
    /// Returns why <paramref name="value"/> can never be a revision or could be misread as a Git
    /// option, or <see langword="null"/> when it is acceptable. Whether the revision exists is
    /// checked later, by Git itself.
    /// </summary>
    public static string? GetRefError(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Git revision must not be empty.";
        }

        var trimmed = value.Trim();

        if (trimmed.Length > MaxRefLength)
        {
            return $"Git revision must not exceed {MaxRefLength} characters.";
        }

        if (trimmed.StartsWith('-'))
        {
            return $"Git revision must not start with '-': '{trimmed}'.";
        }

        if (trimmed.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)))
        {
            return $"Git revision must not contain whitespace or control characters: '{trimmed}'.";
        }

        return null;
    }

    private static string ValidateRef(string value, string paramName) =>
        GetRefError(value) is { } error
            ? throw new ArgumentException(error, paramName)
            : value.Trim();
}
