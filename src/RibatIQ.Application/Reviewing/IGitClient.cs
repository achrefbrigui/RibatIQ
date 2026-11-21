namespace RibatIQ.Application.Reviewing;

/// <summary>Read-only access to a local Git repository.</summary>
public interface IGitClient
{
    /// <summary>Returns the absolute root of the repository containing <paramref name="workingDirectory"/>.</summary>
    /// <exception cref="InvalidInputException">The directory is not inside a Git repository.</exception>
    /// <exception cref="OperationFailedException">Git is missing or failed.</exception>
    Task<string> GetRepositoryRootAsync(string workingDirectory, CancellationToken cancellationToken);

    /// <summary>
    /// Returns the unified diff (zero context lines) of the changes on <paramref name="headRef"/>
    /// since its merge base with <paramref name="baseRef"/>, like a pull request.
    /// </summary>
    /// <exception cref="InvalidInputException">A revision does not exist or history is missing (shallow clone).</exception>
    /// <exception cref="OperationFailedException">Git is missing or failed.</exception>
    Task<string> GetDiffAsync(string repositoryRoot, string baseRef, string headRef, CancellationToken cancellationToken);

    /// <summary>
    /// Returns the size in bytes of <paramref name="path"/> at <paramref name="revision"/>, or
    /// <see langword="null"/> when the file does not exist there.
    /// </summary>
    /// <exception cref="OperationFailedException">Git is missing or failed.</exception>
    Task<long?> GetFileSizeAsync(string repositoryRoot, string revision, string path, CancellationToken cancellationToken);

    /// <summary>Returns the content of <paramref name="path"/> at <paramref name="revision"/>, without touching the working tree.</summary>
    /// <exception cref="OperationFailedException">Git is missing or failed.</exception>
    Task<string> ReadFileAsync(string repositoryRoot, string revision, string path, CancellationToken cancellationToken);
}
