using System.Text;

using RibatIQ.Application.Instructions;

namespace RibatIQ.Infrastructure.Instructions;

/// <summary>
/// Reads a project instructions file from disk without trusting it: symbolic links are not followed,
/// oversized files are not read, and only strict UTF-8 is accepted. Problems are reported, never thrown.
/// </summary>
public static class FileSystemInstructionsReader
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>Reads <see cref="ProjectInstructionsLoader.FileName"/> in <paramref name="directory"/>.</summary>
    public static Task<InstructionsReadResult> ReadFromDirectoryAsync(string directory, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        return ReadAsync(Path.Combine(directory, ProjectInstructionsLoader.FileName), ProjectInstructionsLoader.FileName, cancellationToken);
    }

    /// <summary>Reads the file at <paramref name="path"/>, reported to users as <paramref name="source"/>.</summary>
    public static async Task<InstructionsReadResult> ReadAsync(string path, string source, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);

        try
        {
            var info = new FileInfo(path);

            if (Directory.Exists(path))
            {
                return new InstructionsReadResult(source, null, InstructionsStatus.Invalid, "it is a directory");
            }

            if (!info.Exists)
            {
                return new InstructionsReadResult(source, null, InstructionsStatus.NotFound);
            }

            if (info.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                return new InstructionsReadResult(source, null, InstructionsStatus.Invalid, "symbolic links are not followed");
            }

            if (info.Length > ProjectInstructionsLoader.MaxFileBytes)
            {
                return new InstructionsReadResult(source, null, InstructionsStatus.TooLarge);
            }

            var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);

            // The file may have grown between the size check and the read.
            if (bytes.Length > ProjectInstructionsLoader.MaxFileBytes)
            {
                return new InstructionsReadResult(source, null, InstructionsStatus.TooLarge);
            }

            var preamble = StrictUtf8.Preamble;
            var offset = bytes.AsSpan().StartsWith(preamble) ? preamble.Length : 0;
            return new InstructionsReadResult(source, StrictUtf8.GetString(bytes, offset, bytes.Length - offset));
        }
        catch (DecoderFallbackException)
        {
            return new InstructionsReadResult(source, null, InstructionsStatus.Invalid, "invalid UTF-8");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new InstructionsReadResult(source, null, InstructionsStatus.Unreadable, ex.GetType().Name);
        }
    }
}
