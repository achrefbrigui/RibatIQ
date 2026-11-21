using System.Text;

using RibatIQ.Application;
using RibatIQ.Application.Reporting;
using RibatIQ.Infrastructure.Reporting;

namespace RibatIQ.Cli.Output;

/// <summary>Writes a report in the requested format to standard output or to a file.</summary>
internal static class ReportPublisher
{
    // A report must never replace source or build files, even by mistake.
    private static readonly string[] ProtectedExtensions =
        [".cs", ".csproj", ".sln", ".slnx", ".props", ".targets", ".config", ".editorconfig", ".yml", ".yaml"];

    public static IReportWriter CreateWriter(string format) => format switch
    {
        "json" => new JsonReportWriter(),
        _ => new ConsoleReportWriter(),
    };

    /// <exception cref="InvalidInputException">The output path is not acceptable.</exception>
    /// <exception cref="OperationFailedException">The file could not be written.</exception>
    public static async Task PublishAsync(
        AnalysisReport report, string format, string? outputPath, CliContext context, CancellationToken cancellationToken)
    {
        var writer = CreateWriter(format);

        if (outputPath is null)
        {
            await writer.WriteAsync(report, context.Output, cancellationToken).ConfigureAwait(false);
            return;
        }

        var fullPath = Path.GetFullPath(outputPath, context.CurrentDirectory);
        if (ProtectedExtensions.Contains(Path.GetExtension(fullPath), StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidInputException(
                $"Refusing to write the report to '{outputPath}': that file type is reserved for source or build files.");
        }

        if (Directory.Exists(fullPath))
        {
            throw new InvalidInputException($"The output path '{outputPath}' is a directory.");
        }

        var directory = Path.GetDirectoryName(fullPath)!;
        if (!Directory.Exists(directory))
        {
            throw new InvalidInputException($"The output directory for '{outputPath}' does not exist.");
        }

        // Write to a temporary file first so a failure never leaves a truncated report behind.
        var temporary = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            var stream = new StreamWriter(temporary, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            await using (stream.ConfigureAwait(false))
            {
                await writer.WriteAsync(report, stream, cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporary, fullPath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new OperationFailedException($"Could not write the report to '{outputPath}': {ex.Message}", ex);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }

        await context.Error.WriteLineAsync($"Report written to {outputPath} ({writer.Format}).").ConfigureAwait(false);
    }

    /// <summary>The scan target as shown to users: relative to the working directory when inside it.</summary>
    public static string DisplayPath(string fullPath, string currentDirectory)
    {
        var relative = Path.GetRelativePath(currentDirectory, fullPath);
        return relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative)
            ? fullPath
            : relative.Replace('\\', '/');
    }
}
