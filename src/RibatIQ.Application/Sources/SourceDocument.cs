using RibatIQ.Domain.Findings;

namespace RibatIQ.Application.Sources;

/// <summary>An in-memory source file identified by its repository-relative path.</summary>
public sealed record SourceDocument
{
    public SourceDocument(string path, string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        Path = RepositoryPath.Normalize(path, nameof(path));
        Text = text;
    }

    /// <summary>Repository-relative path using forward slashes.</summary>
    public string Path { get; }

    public string Text { get; }
}
