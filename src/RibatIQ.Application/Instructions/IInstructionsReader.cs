namespace RibatIQ.Application.Instructions;

/// <summary>Reads project instructions from the file system. Problems are reported in the result, never thrown.</summary>
public interface IInstructionsReader
{
    /// <summary>Reads <see cref="ProjectInstructionsLoader.FileName"/> in <paramref name="directory"/>.</summary>
    Task<InstructionsReadResult> ReadFromDirectoryAsync(string directory, CancellationToken cancellationToken);

    /// <summary>Reads the file at <paramref name="path"/>, reported to users as <paramref name="source"/>.</summary>
    Task<InstructionsReadResult> ReadFileAsync(string path, string source, CancellationToken cancellationToken);
}
