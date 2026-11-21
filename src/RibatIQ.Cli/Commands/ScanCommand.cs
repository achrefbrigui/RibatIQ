using System.CommandLine;

using RibatIQ.Application.Scanning;

namespace RibatIQ.Cli.Commands;

/// <summary><c>ribat scan &lt;path&gt;</c>: analyze a solution, project or directory.</summary>
internal static class ScanCommand
{
    private static readonly string[] SupportedFileExtensions = [".sln", ".slnx", ".csproj"];

    public static Command Create(CliContext context)
    {
        var pathArgument = new Argument<string>("path")
        {
            Description = "Solution (.sln, .slnx), project (.csproj) or directory to scan.",
        };

        var command = new Command("scan", "Scan a .NET solution, project or directory.")
        {
            // Hidden until the analysis engine is wired in, so --help only lists working commands.
            Hidden = true,
        };
        command.Arguments.Add(pathArgument);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var path = parseResult.GetValue(pathArgument)!;

            if (!TryResolveTarget(context, path, out var request, out var error))
            {
                await context.Error.WriteLineAsync(error).ConfigureAwait(false);
                return ExitCodes.InvalidUsage;
            }

            return await CommandResults.EngineNotAvailableAsync(context, "scan", request.TargetPath).ConfigureAwait(false);
        });

        return command;
    }

    internal static bool TryResolveTarget(CliContext context, string path, out ScanRequest request, out string error)
    {
        request = null!;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(path))
        {
            error = "The scan path must not be empty.";
            return false;
        }

        var fullPath = Path.GetFullPath(path, context.CurrentDirectory);

        if (Directory.Exists(fullPath))
        {
            request = new ScanRequest(fullPath);
            return true;
        }

        if (!File.Exists(fullPath))
        {
            error = $"The scan path '{path}' does not exist.";
            return false;
        }

        var extension = Path.GetExtension(fullPath);
        if (!SupportedFileExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            error = $"Unsupported file '{path}'. Expected a directory or a {string.Join(", ", SupportedFileExtensions)} file.";
            return false;
        }

        request = new ScanRequest(fullPath);
        return true;
    }
}
