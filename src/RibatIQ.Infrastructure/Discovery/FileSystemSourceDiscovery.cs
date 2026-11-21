using RibatIQ.Application;
using RibatIQ.Application.Scanning;
using RibatIQ.Application.Sources;

namespace RibatIQ.Infrastructure.Discovery;

/// <summary>
/// Discovers C# projects and source files on the local file system.
/// </summary>
/// <remarks>
/// Source files are found by convention, like SDK-style projects: every <c>*.cs</c> file under the
/// project directory, excluding nested projects. Explicit <c>Compile</c> items in project files are not
/// evaluated. Symbolic links and other reparse points are never followed, so discovery cannot escape
/// the root directory. Files are only read, never modified.
/// </remarks>
public sealed class FileSystemSourceDiscovery : ISourceDiscovery
{
    public const long DefaultMaxFileSizeBytes = 1024 * 1024;
    public const int DefaultMaxFiles = 5_000;

    private static readonly EnumerationOptions SingleLevel = new()
    {
        RecurseSubdirectories = false,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.Device | FileAttributes.Offline,
        MatchCasing = MatchCasing.CaseInsensitive,
    };

    private readonly long _maxFileSizeBytes;
    private readonly int _maxFiles;

    public FileSystemSourceDiscovery(long maxFileSizeBytes = DefaultMaxFileSizeBytes, int maxFiles = DefaultMaxFiles)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxFileSizeBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxFiles);

        _maxFileSizeBytes = maxFileSizeBytes;
        _maxFiles = maxFiles;
    }

    public async Task<DiscoveryResult> DiscoverAsync(ScanRequest request, PathFilter filter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(filter);

        var target = Path.GetFullPath(request.TargetPath);
        var warnings = new List<string>();

        string root;
        List<string> projectFiles;
        List<string> sourceFiles;

        if (Directory.Exists(target))
        {
            root = target;
            var walk = Walk(root, root, filter, stopAtNestedProjects: false);
            projectFiles = walk.Projects;
            sourceFiles = walk.Sources;
        }
        else if (File.Exists(target) && target.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
        {
            root = Path.GetDirectoryName(target)!;
            projectFiles = [target];
            sourceFiles = Walk(root, root, filter, stopAtNestedProjects: true).Sources;
        }
        else if (File.Exists(target))
        {
            root = Path.GetDirectoryName(target)!;
            (projectFiles, sourceFiles) = await DiscoverSolutionAsync(target, root, filter, warnings, cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            throw new InvalidInputException($"The scan target '{request.TargetPath}' does not exist.");
        }

        var documents = await ReadDocumentsAsync(root, sourceFiles, warnings, cancellationToken).ConfigureAwait(false);

        var projects = projectFiles
            .Select(p => new DiscoveredProject(Path.GetFileNameWithoutExtension(p), ToRelative(root, p)))
            .OrderBy(p => p.Path, StringComparer.Ordinal)
            .ToArray();

        return new DiscoveryResult(root, projects, documents, warnings);
    }

    private static async Task<(List<string> Projects, List<string> Sources)> DiscoverSolutionAsync(
        string solutionPath, string root, PathFilter filter, List<string> warnings, CancellationToken cancellationToken)
    {
        var projects = new List<string>();
        var sources = new List<string>();

        foreach (var projectPath in await SolutionFileReader.ReadProjectPathsAsync(solutionPath, cancellationToken).ConfigureAwait(false))
        {
            if (!IsUnder(root, projectPath))
            {
                warnings.Add($"Skipped project outside the solution directory: {Path.GetFileName(projectPath)}");
                continue;
            }

            var relative = ToRelative(root, projectPath);
            if (!File.Exists(projectPath))
            {
                warnings.Add($"Project listed in the solution was not found: {relative}");
                continue;
            }

            projects.Add(projectPath);
            sources.AddRange(Walk(root, Path.GetDirectoryName(projectPath)!, filter, stopAtNestedProjects: true).Sources);
        }

        return (projects, sources.Distinct(StringComparer.OrdinalIgnoreCase).ToList());
    }

    /// <summary>
    /// Walks <paramref name="start"/> breadth-first, pruning excluded directories. With
    /// <paramref name="stopAtNestedProjects"/>, subdirectories containing their own project are skipped.
    /// </summary>
    private static (List<string> Projects, List<string> Sources) Walk(string root, string start, PathFilter filter, bool stopAtNestedProjects)
    {
        var projects = new List<string>();
        var sources = new List<string>();
        var pending = new Queue<string>();
        pending.Enqueue(start);

        while (pending.Count > 0)
        {
            var directory = pending.Dequeue();

            foreach (var file in Directory.EnumerateFiles(directory, "*", SingleLevel))
            {
                var relative = ToRelative(root, file);
                if (file.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) && !filter.IsDirectoryExcluded(ToRelative(root, directory)))
                {
                    projects.Add(file);
                }
                else if (file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) && filter.IsIncluded(relative))
                {
                    sources.Add(file);
                }
            }

            foreach (var subdirectory in Directory.EnumerateDirectories(directory, "*", SingleLevel))
            {
                if (filter.IsDirectoryExcluded(ToRelative(root, subdirectory)))
                {
                    continue;
                }

                if (stopAtNestedProjects && Directory.EnumerateFiles(subdirectory, "*.csproj", SingleLevel).Any())
                {
                    continue;
                }

                pending.Enqueue(subdirectory);
            }
        }

        return (projects, sources);
    }

    private async Task<IReadOnlyList<SourceDocument>> ReadDocumentsAsync(
        string root, List<string> files, List<string> warnings, CancellationToken cancellationToken)
    {
        var ordered = files
            .Select(f => (Full: f, Relative: ToRelative(root, f)))
            .OrderBy(f => f.Relative, StringComparer.Ordinal)
            .ToList();

        if (ordered.Count > _maxFiles)
        {
            warnings.Add($"Found {ordered.Count} source files; only the first {_maxFiles} are analyzed. Narrow the scan with --include or --exclude.");
            ordered = ordered.Take(_maxFiles).ToList();
        }

        var documents = new List<SourceDocument>(ordered.Count);
        foreach (var (full, relative) in ordered)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (new FileInfo(full).Length > _maxFileSizeBytes)
            {
                warnings.Add($"Skipped file larger than {_maxFileSizeBytes / 1024} KB: {relative}");
                continue;
            }

            try
            {
                var text = await File.ReadAllTextAsync(full, cancellationToken).ConfigureAwait(false);
                documents.Add(new SourceDocument(relative, text));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                warnings.Add($"Could not read {relative}: {ex.GetType().Name}");
            }
        }

        return documents;
    }

    private static bool IsUnder(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path);
        return relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && !Path.IsPathRooted(relative);
    }

    private static string ToRelative(string root, string path) =>
        Path.GetRelativePath(root, path).Replace('\\', '/');
}
