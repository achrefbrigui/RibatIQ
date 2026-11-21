using System.Text;

using RibatIQ.Application.Instructions;
using RibatIQ.Infrastructure.Git;
using RibatIQ.Infrastructure.Instructions;

namespace RibatIQ.IntegrationTests;

/// <summary>Reading review-rules.md from disk and from Git, with every kind of malformed input.</summary>
public sealed class ProjectInstructionsReaderTests : IDisposable
{
    private readonly TempDirectory _directory = new();

    public void Dispose() => _directory.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private string FilePath => Path.Combine(_directory.Path, ProjectInstructionsLoader.FileName);

    [Fact]
    public async Task Disk_MissingFile_IsNotFound()
    {
        var result = await LoadFromDiskAsync();

        Assert.Equal(InstructionsStatus.NotFound, result.Status);
        Assert.Null(result.Warning);
    }

    [Fact]
    public async Task Disk_TextFile_IsLoaded_WithBomAndNonAsciiText()
    {
        await File.WriteAllBytesAsync(FilePath, [.. Encoding.UTF8.Preamble, .. Encoding.UTF8.GetBytes("Règles : préférer Result<T> 🙂\n")], Ct);

        var result = await LoadFromDiskAsync();

        Assert.Equal(InstructionsStatus.Loaded, result.Status);
        Assert.Equal("Règles : préférer Result<T> 🙂", result.Instructions!.Text);
    }

    [Fact]
    public async Task Disk_InvalidUtf8_IsRejected()
    {
        await File.WriteAllBytesAsync(FilePath, [0x52, 0x75, 0x6C, 0x65, 0xC3, 0x28, 0xFF], Ct);

        var result = await LoadFromDiskAsync();

        Assert.Equal(InstructionsStatus.Invalid, result.Status);
        Assert.Contains("invalid UTF-8", result.Warning, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Disk_BinaryFile_IsRejected()
    {
        await File.WriteAllBytesAsync(FilePath, [0x89, 0x50, 0x4E, 0x47, 0x00, 0x00, 0x00, 0x0D], Ct);

        Assert.Equal(InstructionsStatus.Invalid, (await LoadFromDiskAsync()).Status);
    }

    [Fact]
    public async Task Disk_EmptyFile_IsIgnoredWithAWarning()
    {
        await File.WriteAllTextAsync(FilePath, "  \n", Ct);

        Assert.Equal(InstructionsStatus.Empty, (await LoadFromDiskAsync()).Status);
    }

    [Fact]
    public async Task Disk_FileAtTheSizeLimit_IsRead_AndOneByteMoreIsNot()
    {
        await File.WriteAllTextAsync(FilePath, new string('a', ProjectInstructionsLoader.MaxFileBytes), Ct);
        Assert.Equal(InstructionsStatus.Truncated, (await LoadFromDiskAsync()).Status);

        await File.WriteAllTextAsync(FilePath, new string('a', ProjectInstructionsLoader.MaxFileBytes + 1), Ct);
        var result = await LoadFromDiskAsync();
        Assert.Equal(InstructionsStatus.TooLarge, result.Status);
        Assert.Null(result.Instructions);
    }

    [Fact]
    public async Task Disk_DirectoryWithTheFileName_IsInvalid()
    {
        Directory.CreateDirectory(FilePath);

        Assert.Equal(InstructionsStatus.Invalid, (await LoadFromDiskAsync()).Status);
    }

    [Fact]
    public async Task Disk_SymbolicLink_IsNotFollowed()
    {
        var target = _directory.WriteFile("secret.txt", "content outside the instructions");
        try
        {
            File.CreateSymbolicLink(FilePath, target);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            Assert.Skip($"Symbolic links cannot be created here: {ex.GetType().Name}.");
        }

        var result = await LoadFromDiskAsync();

        Assert.Equal(InstructionsStatus.Invalid, result.Status);
        Assert.Null(result.Instructions);
    }

    [Fact]
    public async Task Disk_ExplicitPath_UsesTheGivenSourceName()
    {
        var path = _directory.WriteFile("docs/team-rules.md", "Keep controllers thin.");

        var result = ProjectInstructionsLoader.Load(await FileSystemInstructionsReader.ReadAsync(path, "docs/team-rules.md", Ct));

        Assert.Equal("docs/team-rules.md", result.Instructions!.Source);
    }

    [Fact]
    public async Task Git_GetFileSize_ReportsRegularFilesOnly()
    {
        using var repo = new TempGitRepository();
        repo.WriteFile("review-rules.md", "12345");
        repo.WriteFile("docs/review-rules.md/readme.txt", "a directory with the same name");
        repo.Commit("init");
        var git = new GitCli();

        Assert.Equal(5, await git.GetFileSizeAsync(repo.Path, "HEAD", "review-rules.md", Ct));
        Assert.Null(await git.GetFileSizeAsync(repo.Path, "HEAD", "missing.md", Ct));
        Assert.Null(await git.GetFileSizeAsync(repo.Path, "HEAD", "docs/review-rules.md", Ct));
    }

    [Fact]
    public async Task Git_SymbolicLinkNamedLikeTheFile_IsTreatedAsAbsent()
    {
        using var repo = new TempGitRepository();
        repo.WriteFile("placeholder.txt", "x");
        repo.Commit("init");

        // Commit a symbolic link without creating one on disk, so the test works on every platform.
        var target = repo.Git("hash-object", "-w", "placeholder.txt").Trim();
        repo.Git("update-index", "--add", "--cacheinfo", $"120000,{target},review-rules.md");
        repo.Git("commit", "-q", "-m", "link");

        Assert.Null(await new GitCli().GetFileSizeAsync(repo.Path, "HEAD", "review-rules.md", Ct));
        Assert.Equal(InstructionsStatus.NotFound, (await ProjectInstructionsLoader.LoadFromGitAsync(new GitCli(), repo.Path, "HEAD", Ct)).Status);
    }

    [Fact]
    public async Task Git_ReadsTheCommittedFile_NotTheWorkingTree()
    {
        using var repo = new TempGitRepository();
        repo.WriteFile("review-rules.md", "Committed rule.\n");
        repo.Commit("rules");
        repo.WriteFile("review-rules.md", "Uncommitted edit.\n");

        var result = await ProjectInstructionsLoader.LoadFromGitAsync(new GitCli(), repo.Path, "HEAD", Ct);

        Assert.Equal(InstructionsStatus.Loaded, result.Status);
        Assert.Equal("Committed rule.", result.Instructions!.Text);
    }

    [Fact]
    public async Task Git_FileAtAnOlderRevision_IsUsedWhenRequested()
    {
        using var repo = new TempGitRepository();
        repo.WriteFile("review-rules.md", "Version one.");
        var first = repo.Commit("v1");
        repo.WriteFile("review-rules.md", "Version two.");
        repo.Commit("v2");

        var result = await ProjectInstructionsLoader.LoadFromGitAsync(new GitCli(), repo.Path, first, Ct);

        Assert.Equal("Version one.", result.Instructions!.Text);
    }

    [Fact]
    public async Task Git_OversizedFile_IsNotRead()
    {
        using var repo = new TempGitRepository();
        repo.WriteFile("review-rules.md", new string('a', ProjectInstructionsLoader.MaxFileBytes + 10));
        repo.Commit("big");

        var result = await ProjectInstructionsLoader.LoadFromGitAsync(new GitCli(), repo.Path, "HEAD", Ct);

        Assert.Equal(InstructionsStatus.TooLarge, result.Status);
    }

    private async Task<InstructionsLoadResult> LoadFromDiskAsync() =>
        ProjectInstructionsLoader.Load(await FileSystemInstructionsReader.ReadFromDirectoryAsync(_directory.Path, Ct));
}
