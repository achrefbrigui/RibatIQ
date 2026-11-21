# RibatIQ

**Open-source AI code intelligence for .NET.**
*Built for developers who never stop improving. Guarding code. Growing intelligence.*

RibatIQ is a command-line tool that reviews C# code. It combines deterministic static analysis
(Roslyn) with optional AI analysis by the LLM provider of your choice (OpenAI or Anthropic), and
reports findings for people (console) and for tools (JSON, CI exit codes).

- **`ribat scan`** analyzes an existing solution, project or directory, with no pull request needed.
- **`ribat review`** analyzes the changes between two Git revisions and reports only issues on changed lines.

> RibatIQ reports likely issues. It does not prove that code is correct, secure or bug-free, and it
> never modifies your source files.

## Requirements

- .NET SDK 10.0.100 or later.
- Git on the `PATH` for `ribat review`.
- Optionally, an OpenAI or Anthropic API key for AI analysis.

## Installation

RibatIQ is packaged as a .NET tool. Until it is published to NuGet, build and install it from source:

```bash
git clone https://github.com/achrefbrigui/RibatIQ.git
cd RibatIQ
dotnet pack src/RibatIQ.Cli --configuration Release --output artifacts
dotnet tool install --global RibatIQ.Cli --add-source ./artifacts
ribat --version
```

To try it without installing: `dotnet run --project src/RibatIQ.Cli -- <command>`.

## Quick start

```bash
# Static analysis of a solution (.sln/.slnx), a project (.csproj) or a directory
ribat scan path/to/MySolution.sln

# Review what the current branch changed since it diverged from origin/main
ribat review --base origin/main

# Same, with AI analysis and a JSON report, failing on high-severity findings
export ANTHROPIC_API_KEY=...          # PowerShell: $env:ANTHROPIC_API_KEY = "..."
ribat review --base origin/main --provider anthropic --format json --output ribatiq.json --fail-on high
```

## Commands and options

```text
ribat scan <path> [options]
ribat review --base <revision> [--head <revision>] [options]
ribat --help | --version
```

| Option | Description |
|--------|-------------|
| `--provider openai\|anthropic` | Enable AI analysis with this provider. |
| `--model <id>` | Model to use (defaults: `gpt-6.2-astra`, `claude-opus-5-5`). |
| `--no-ai` | Disable AI analysis; nothing is sent to a provider. |
| `--format console\|json` | Report format (default `console`). |
| `--output <file>` | Write the report to a file instead of standard output. |
| `--fail-on none\|info\|low\|medium\|high\|critical` | Exit with code 1 when a finding is at or above this severity (default `none`). |
| `--include <glob>` / `--exclude <glob>` | Files to analyze / skip (repeatable). `bin/`, `obj/` and `.git/` are always skipped. |
| `--config <file>` | Configuration file (default: `ribatiq.json` in the current directory, if present). |
| `--verbose` | Print the effective settings to standard error. |

`review` compares `--head` (default `HEAD`) with its merge base with `--base`, like a pull request, and
reads files at the `--head` revision rather than from the working tree. Run it inside the repository.

## Exit codes

| Code | Meaning |
|------|---------|
| `0` | Completed; no finding met the `--fail-on` threshold. |
| `1` | Completed; at least one finding met the threshold. The report is always written first. |
| `2` | Invalid usage, configuration or input (unknown option, missing file, unknown Git revision, missing API key...). |
| `3` | Operational failure (Git or provider failure, malformed AI response, I/O error). Never reported as a clean result. |

## What is analyzed

**Deterministic analysis** (always on): sources are parsed in memory by Roslyn; nothing is built,
restored or executed.

| Rule | Finding |
|------|---------|
| `CSxxxx` | C# syntax errors. |
| `RIQ1001` | Empty catch block (catch blocks with a comment are accepted). |
| `RIQ1002` | `async void` method (event handlers excluded). |
| `RIQ1003` | `throw ex;` resets the stack trace. |
| `RIQ1004` | Possible hard-coded secret (the value is never printed). |

**AI analysis** (opt-in): the model looks for actionable bugs, security, reliability, concurrency and
design issues, and every finding it returns is validated before it is reported.

| Provider | API key variable | Default model |
|----------|------------------|---------------|
| OpenAI | `OPENAI_API_KEY` | `gpt-6.2-astra` |
| Anthropic | `ANTHROPIC_API_KEY` | `claude-opus-5-5` |

See [Security and privacy](docs/security-and-privacy.md) for what is sent to providers and how model
output is validated.

## Configuration

Settings come from, in order of precedence: command-line options, environment variables
(`RIBATIQ_PROVIDER`, `RIBATIQ_MODEL`, `RIBATIQ_FAIL_ON`), the `ribatiq.json` file, then defaults.
API keys are read only from the environment, never from configuration files.

```json
{
  "version": 1,
  "analysis": {
    "exclude": ["**/Migrations/**", "**/*.g.cs"],
    "failOn": "high"
  },
  "ai": {
    "provider": "anthropic"
  }
}
```

See [Configuration](docs/configuration.md) and [`samples/ribatiq.json`](samples/ribatiq.json).

## Using RibatIQ in CI

```yaml
- uses: actions/checkout@v7
  with:
    fetch-depth: 0          # review needs the history up to the merge base
- uses: actions/setup-dotnet@v6
  with:
    dotnet-version: 10.0.x
- run: ribat review --base origin/${{ github.base_ref }} --format json --output ribatiq.json --fail-on high
```

The JSON report (`schemaVersion` 1) contains the findings with stable IDs, a summary, the AI
provider and usage, and a `gate` object with the threshold outcome.

## Limitations

- C# only. Diagnostics that need project references (types, overloads, nullability) are not reported yet:
  sources are analyzed without building them.
- Source files are found by convention (all `*.cs` under a project directory); explicit `Compile`
  items in project files are not evaluated.
- AI findings are heuristic. Their confidence score is the model's own estimate, not a probability.
- There is no suppression or baseline mechanism yet; finding IDs are stable to make one possible.
- Results are not published to pull requests yet.

## Development

```bash
dotnet build
dotnet test
```

Tests need no API keys and no network access beyond the NuGet restore; provider adapters are tested
against stubbed HTTP. Opt-in live tests run with `RIBATIQ_LIVE_TESTS=1` and an API key (they are billed).
See [Architecture](docs/architecture.md) and [Contributing](CONTRIBUTING.md).

## License

[MIT](LICENSE)
