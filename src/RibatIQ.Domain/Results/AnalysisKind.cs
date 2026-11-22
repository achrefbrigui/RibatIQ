namespace RibatIQ.Domain.Results;

/// <summary>The workflow that produced an <see cref="AnalysisResult"/>.</summary>
public enum AnalysisKind
{
    /// <summary>Full scan of a solution or directory.</summary>
    Scan = 0,

    /// <summary>Review of the changes between two Git refs.</summary>
    Review = 1,
}
