namespace RibatIQ.Application.Scanning;

/// <summary>Input of the scan workflow: the solution, project or directory to analyze.</summary>
public sealed record ScanRequest
{
    public ScanRequest(string targetPath)
    {
        if (string.IsNullOrWhiteSpace(targetPath))
        {
            throw new ArgumentException("Scan target path must not be empty.", nameof(targetPath));
        }

        TargetPath = targetPath;
    }

    /// <summary>Full path of an existing solution, project file or directory.</summary>
    public string TargetPath { get; }
}
