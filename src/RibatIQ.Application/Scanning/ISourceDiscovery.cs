using RibatIQ.Application.Sources;

namespace RibatIQ.Application.Scanning;

/// <summary>Finds the projects and C# source files to analyze for a scan.</summary>
public interface ISourceDiscovery
{
    /// <exception cref="InvalidInputException">The target cannot be read as a solution, project or directory.</exception>
    Task<DiscoveryResult> DiscoverAsync(ScanRequest request, PathFilter filter, CancellationToken cancellationToken);
}

/// <summary>A project found during discovery.</summary>
/// <param name="Name">Project name.</param>
/// <param name="Path">Repository-relative path of the project file.</param>
public sealed record DiscoveredProject(string Name, string Path);

/// <summary>Outcome of source discovery.</summary>
/// <param name="RootDirectory">Absolute directory that finding paths are relative to.</param>
/// <param name="Projects">Projects found, ordered by path.</param>
/// <param name="Documents">Source files to analyze, ordered by path.</param>
/// <param name="Warnings">Non-fatal issues, such as missing projects or skipped files.</param>
public sealed record DiscoveryResult(
    string RootDirectory,
    IReadOnlyList<DiscoveredProject> Projects,
    IReadOnlyList<SourceDocument> Documents,
    IReadOnlyList<string> Warnings);
