namespace RibatIQ.IntegrationTests;

/// <summary>A uniquely named directory under the system temp folder, deleted on dispose.</summary>
internal sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ribatiq-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string WriteFile(string relativePath, string content)
    {
        var fullPath = System.IO.Path.Combine(Path, relativePath);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
        return fullPath;
    }

    /// <summary>Creates a temp directory holding a copy of a fixture from the test output's Fixtures folder.</summary>
    public static TempDirectory FromFixture(string fixtureName)
    {
        var source = System.IO.Path.Combine(AppContext.BaseDirectory, "Fixtures", fixtureName);
        if (!Directory.Exists(source))
        {
            throw new DirectoryNotFoundException($"Fixture '{fixtureName}' not found at '{source}'.");
        }

        var temp = new TempDirectory();
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var destination = System.IO.Path.Combine(temp.Path, System.IO.Path.GetRelativePath(source, file));
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(destination)!);
            File.Copy(file, destination);
        }

        return temp;
    }

    /// <summary>SHA-256 of every file under the directory, keyed by relative path.</summary>
    public IReadOnlyDictionary<string, string> Snapshot() =>
        Directory.EnumerateFiles(Path, "*", SearchOption.AllDirectories)
            .ToDictionary(
                f => System.IO.Path.GetRelativePath(Path, f),
                f => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(f))));

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // Best effort: a leftover temp directory must not fail a test.
        }
    }
}
