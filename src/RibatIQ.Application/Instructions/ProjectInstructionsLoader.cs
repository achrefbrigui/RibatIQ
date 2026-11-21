using System.Globalization;

using RibatIQ.Application.Reviewing;
using RibatIQ.Domain.Policies;

namespace RibatIQ.Application.Instructions;

/// <summary>Outcome of looking for project instructions.</summary>
public enum InstructionsStatus
{
    /// <summary>The instructions were loaded in full.</summary>
    Loaded = 0,

    /// <summary>The instructions were loaded but cut at <see cref="ProjectInstructions.MaxLength"/> characters.</summary>
    Truncated,

    /// <summary>There is no instructions file; this is normal.</summary>
    NotFound,

    /// <summary>The file exists but contains no usable text.</summary>
    Empty,

    /// <summary>The file is larger than <see cref="ProjectInstructionsLoader.MaxFileBytes"/> and was not read.</summary>
    TooLarge,

    /// <summary>The file is not UTF-8 text (binary content, invalid encoding, or not a regular file).</summary>
    Invalid,

    /// <summary>The file could not be read.</summary>
    Unreadable,
}

/// <summary>What a reader found: either the text, or the problem that prevented reading it.</summary>
/// <param name="Source">User-facing name of the file, for example <c>review-rules.md</c>.</param>
/// <param name="Text">Decoded text, when the file was read.</param>
/// <param name="Problem">Why there is no text (<see cref="InstructionsStatus.NotFound"/>, too large, invalid or unreadable).</param>
/// <param name="Detail">Short, non-sensitive detail about the problem (never file content).</param>
public sealed record InstructionsReadResult(string Source, string? Text, InstructionsStatus? Problem = null, string? Detail = null);

/// <summary>Project instructions ready to use, or why there are none.</summary>
/// <param name="Instructions">The loaded instructions, or <see langword="null"/>.</param>
/// <param name="Status">What happened.</param>
/// <param name="Message">User-facing explanation.</param>
public sealed record InstructionsLoadResult(ProjectInstructions? Instructions, InstructionsStatus Status, string Message)
{
    /// <summary>A message worth surfacing as a warning: the file exists but was not (fully) used.</summary>
    public string? Warning => Status is InstructionsStatus.Loaded or InstructionsStatus.NotFound ? null : Message;
}

/// <summary>
/// Loads project-specific review instructions (<c>review-rules.md</c>). The file is untrusted: it is size
/// limited, must be UTF-8 text, and is normalized by <see cref="ProjectInstructions"/>. Any problem is
/// reported and the review continues without instructions; a missing file is normal.
/// </summary>
public static class ProjectInstructionsLoader
{
    /// <summary>Instructions file looked for at the root of the reviewed repository or target.</summary>
    public const string FileName = "review-rules.md";

    /// <summary>Files larger than this are not read at all.</summary>
    public const int MaxFileBytes = 64 * 1024;

    public static InstructionsLoadResult Load(InstructionsReadResult read)
    {
        ArgumentNullException.ThrowIfNull(read);

        var source = read.Source;

        if (read.Problem is { } problem)
        {
            return new InstructionsLoadResult(null, problem, problem switch
            {
                InstructionsStatus.NotFound => $"No {source}; the review runs without project instructions.",
                InstructionsStatus.TooLarge => string.Create(CultureInfo.InvariantCulture,
                    $"{source} is larger than {MaxFileBytes / 1024} KB and was ignored."),
                InstructionsStatus.Invalid => $"{source} is not UTF-8 text and was ignored{Suffix(read.Detail)}.",
                _ => $"{source} could not be read and was ignored{Suffix(read.Detail)}.",
            });
        }

        var text = read.Text ?? string.Empty;

        // NUL characters mean binary content; U+FFFD means bytes that were not valid UTF-8.
        if (text.Contains('\0', StringComparison.Ordinal) || text.Contains('�', StringComparison.Ordinal))
        {
            return new InstructionsLoadResult(null, InstructionsStatus.Invalid, $"{source} is not UTF-8 text and was ignored.");
        }

        var instructions = ProjectInstructions.Create(text, source);
        if (instructions is null)
        {
            return new InstructionsLoadResult(null, InstructionsStatus.Empty, $"{source} is empty and was ignored.");
        }

        return instructions.Truncated
            ? new InstructionsLoadResult(instructions, InstructionsStatus.Truncated, string.Create(CultureInfo.InvariantCulture,
                $"{source} is longer than {ProjectInstructions.MaxLength} characters; only the beginning is used."))
            : new InstructionsLoadResult(instructions, InstructionsStatus.Loaded, $"Loaded project instructions from {source}.");
    }

    /// <summary>Loads <see cref="FileName"/> from the repository root as it is at <paramref name="revision"/>.</summary>
    public static async Task<InstructionsLoadResult> LoadFromGitAsync(
        IGitClient git, string repositoryRoot, string revision, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(git);

        var source = FileName;
        try
        {
            var size = await git.GetFileSizeAsync(repositoryRoot, revision, FileName, cancellationToken).ConfigureAwait(false);
            if (size is null)
            {
                return Load(new InstructionsReadResult(source, null, InstructionsStatus.NotFound));
            }

            if (size > MaxFileBytes)
            {
                return Load(new InstructionsReadResult(source, null, InstructionsStatus.TooLarge));
            }

            var text = await git.ReadFileAsync(repositoryRoot, revision, FileName, cancellationToken).ConfigureAwait(false);
            return Load(new InstructionsReadResult(source, text));
        }
        catch (OperationFailedException)
        {
            return Load(new InstructionsReadResult(source, null, InstructionsStatus.Unreadable, "Git could not read it"));
        }
    }

    private static string Suffix(string? detail) => string.IsNullOrWhiteSpace(detail) ? string.Empty : $" ({detail.Trim()})";
}
