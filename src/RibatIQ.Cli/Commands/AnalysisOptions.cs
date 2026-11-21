using System.CommandLine;

using RibatIQ.Application;
using RibatIQ.Application.Configuration;
using RibatIQ.Application.Reporting;
using RibatIQ.Cli.Configuration;
using RibatIQ.Cli.Output;

namespace RibatIQ.Cli.Commands;

/// <summary>Options shared by <c>scan</c> and <c>review</c>, and their translation into settings.</summary>
internal sealed class AnalysisOptions
{
    public const string ConsoleFormat = "console";
    public const string JsonFormat = "json";

    public Option<string> Provider { get; } = new("--provider")
    {
        Description = $"Enable AI analysis with this provider: {string.Join(", ", LlmProviders.All)}. Reads OPENAI_API_KEY or ANTHROPIC_API_KEY. Source excerpts are sent to the provider.",
    };

    public Option<string> Model { get; } = new("--model")
    {
        Description = "Model to use. Defaults: openai gpt-6.2-astra, anthropic claude-opus-5-5.",
    };

    public Option<bool> NoAi { get; } = new("--no-ai")
    {
        Description = "Disable AI analysis; run deterministic analysis only. Nothing is sent to an LLM provider.",
    };

    public Option<string> Format { get; } = new("--format")
    {
        Description = "Report format: console (human-readable) or json (versioned, for tools and CI).",
        DefaultValueFactory = _ => ConsoleFormat,
    };

    public Option<FileInfo> Output { get; } = new("--output")
    {
        Description = "Write the report to this file instead of standard output. Source and build files are never overwritten.",
    };

    public Option<FileInfo> Config { get; } = new("--config")
    {
        Description = $"Configuration file. Defaults to {ConfigFileLoader.DefaultFileName} in the current directory, when present.",
    };

    public Option<string> FailOn { get; } = new("--fail-on")
    {
        Description = "Exit with code 1 when a finding at or above this severity is reported, after the report is written. Default: none.",
    };

    public Option<string[]> Include { get; } = new("--include")
    {
        Description = "Glob of files to analyze. Repeatable. Defaults to all supported files.",
    };

    public Option<string[]> Exclude { get; } = new("--exclude")
    {
        Description = "Glob of files to skip, in addition to bin/, obj/ and .git/. Repeatable.",
    };

    public Option<bool> Verbose { get; } = new("--verbose")
    {
        Description = "Print diagnostic details to standard error. Never prints secrets or source code.",
    };

    public AnalysisOptions()
    {
        Provider.AcceptOnlyFromAmong([.. LlmProviders.All]);
        Format.AcceptOnlyFromAmong(ConsoleFormat, JsonFormat);
        FailOn.AcceptOnlyFromAmong([.. FailPolicy.AcceptedValues]);
    }

    /// <summary>
    /// Writes the report in the requested format and destination, then applies the failure threshold.
    /// The threshold never prevents the report from being produced.
    /// </summary>
    public async Task<int> PublishAsync(AnalysisReport report, ParseResult parseResult, CliContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(parseResult);

        await ReportPublisher.PublishAsync(
            report,
            parseResult.GetValue(Format) ?? ConsoleFormat,
            parseResult.GetValue(Output)?.ToString(),
            context,
            cancellationToken).ConfigureAwait(false);

        return report.ThresholdExceeded ? ExitCodes.FindingsAtOrAboveThreshold : ExitCodes.Success;
    }

    public void AddTo(Command command)
    {
        ArgumentNullException.ThrowIfNull(command);

        foreach (var option in new Option[] { Provider, Model, NoAi, Format, Output, Config, FailOn, Include, Exclude, Verbose })
        {
            command.Options.Add(option);
        }

        command.Validators.Add(result =>
        {
            if (result.GetValue(NoAi) && (result.GetResult(Provider) is not null || result.GetResult(Model) is not null))
            {
                result.AddError("--no-ai cannot be combined with --provider or --model.");
            }
        });
    }

    /// <summary>Settings explicitly given on the command line.</summary>
    public SettingsLayer ToSettingsLayer(ParseResult parseResult)
    {
        ArgumentNullException.ThrowIfNull(parseResult);

        FailPolicy? failOn = null;
        if (parseResult.GetValue(FailOn) is { } failOnValue && FailPolicy.TryParse(failOnValue, out var parsed))
        {
            failOn = parsed;
        }

        return new SettingsLayer("command line")
        {
            Provider = parseResult.GetValue(Provider),
            Model = parseResult.GetValue(Model),
            AiEnabled = parseResult.GetValue(NoAi) ? false : null,
            FailOn = failOn,
            Include = parseResult.GetValue(Include),
            Exclude = parseResult.GetValue(Exclude),
        };
    }

    /// <summary>
    /// Resolves effective settings: command line, then environment, then configuration file, then defaults.
    /// </summary>
    /// <exception cref="ConfigurationException">Settings are invalid or inconsistent.</exception>
    public async Task<RibatSettings> ResolveSettingsAsync(ParseResult parseResult, CliContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parseResult);
        ArgumentNullException.ThrowIfNull(context);

        var commandLine = ToSettingsLayer(parseResult);
        var environment = EnvironmentSettings.Read(context.GetEnvironmentVariable);
        var configFile = await ConfigFileLoader
            .LoadAsync(parseResult.GetValue(Config)?.ToString(), context.CurrentDirectory, cancellationToken)
            .ConfigureAwait(false);

        return SettingsResolver.Resolve([commandLine, environment, configFile]);
    }

    /// <summary>
    /// Resolves settings and runs <paramref name="execute"/> with them. Configuration errors are
    /// reported on standard error and mapped to <see cref="ExitCodes.InvalidUsage"/>.
    /// </summary>
    public async Task<int> RunWithSettingsAsync(
        ParseResult parseResult,
        CliContext context,
        Func<RibatSettings, Task<int>> execute,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(execute);

        RibatSettings settings;
        try
        {
            settings = await ResolveSettingsAsync(parseResult, context, cancellationToken).ConfigureAwait(false);
        }
        catch (ConfigurationException ex)
        {
            await context.Error.WriteLineAsync($"Configuration error: {ex.Message}").ConfigureAwait(false);
            return ExitCodes.InvalidUsage;
        }

        await WriteVerboseAsync(parseResult, context, settings).ConfigureAwait(false);

        if (settings.AiEnabled)
        {
            // Privacy notice before anything is sent: users must know their code leaves the machine.
            await context.Error.WriteLineAsync(
                $"Note: AI analysis is enabled. Excerpts of the analyzed source code will be sent to {settings.Provider} " +
                $"(model: {settings.Model ?? "provider default"}). Use --no-ai to keep the code on this machine.").ConfigureAwait(false);
        }

        try
        {
            return await execute(settings).ConfigureAwait(false);
        }
        catch (ConfigurationException ex)
        {
            await context.Error.WriteLineAsync($"Configuration error: {ex.Message}").ConfigureAwait(false);
            return ExitCodes.InvalidUsage;
        }
        catch (InvalidInputException ex)
        {
            await context.Error.WriteLineAsync($"Error: {ex.Message}").ConfigureAwait(false);
            return ExitCodes.InvalidUsage;
        }
        catch (OperationFailedException ex)
        {
            await context.Error.WriteLineAsync($"Error: {ex.Message}").ConfigureAwait(false);
            return ExitCodes.OperationalFailure;
        }
    }

    /// <summary>Writes the effective settings to standard error when <c>--verbose</c> is set.</summary>
    public async Task WriteVerboseAsync(ParseResult parseResult, CliContext context, RibatSettings settings)
    {
        ArgumentNullException.ThrowIfNull(parseResult);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(settings);

        if (!parseResult.GetValue(Verbose))
        {
            return;
        }

        var error = context.Error;
        await error.WriteLineAsync($"[verbose] AI analysis: {(settings.AiEnabled ? "enabled" : "disabled")}").ConfigureAwait(false);
        if (settings.AiEnabled)
        {
            await error.WriteLineAsync($"[verbose] Provider: {settings.Provider}").ConfigureAwait(false);
            await error.WriteLineAsync($"[verbose] Model: {settings.Model ?? "(provider default)"}").ConfigureAwait(false);
        }

        await error.WriteLineAsync($"[verbose] Fail on: {settings.FailOn}").ConfigureAwait(false);
        await error.WriteLineAsync($"[verbose] Include: {(settings.Include.Count == 0 ? "(all supported files)" : string.Join(", ", settings.Include))}").ConfigureAwait(false);
        await error.WriteLineAsync($"[verbose] Exclude: {string.Join(", ", settings.Exclude)}").ConfigureAwait(false);
        await error.WriteLineAsync($"[verbose] Format: {parseResult.GetValue(Format)}").ConfigureAwait(false);
    }
}
