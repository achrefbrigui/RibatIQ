# RibatIQ

**Open-source AI code intelligence for .NET.**
*Built for developers who never stop improving. Guarding code. Growing intelligence.*

> **Status: early development.** `scan` and `review` run deterministic static analysis.
> AI analysis (OpenAI, Anthropic), JSON reports and the `--fail-on` threshold are not available yet.

## Requirements

- .NET SDK 10.0.100 or later (pinned in `global.json`, rolls forward to the latest feature band).
- Git on the `PATH` for `ribat review`.

## Build and test

```bash
dotnet build
dotnet test
```

Tests require no API keys and no network access beyond the initial NuGet restore.

## Run

```bash
dotnet run --project src/RibatIQ.Cli -- --help

# Scan a solution (.sln/.slnx), a project (.csproj) or a directory
dotnet run --project src/RibatIQ.Cli -- scan path/to/MySolution.sln

# Review the changes of the current branch since it diverged from origin/main (run inside the repository)
dotnet run --project src/RibatIQ.Cli -- review --base origin/main --head HEAD
```

Useful options: `--exclude <glob>` / `--include <glob>` (repeatable), `--config <file>`, `--verbose`.

## What the analysis covers

- **C# syntax errors** reported by the Roslyn parser.
- **RibatIQ rules**: `RIQ1001` empty catch block, `RIQ1002` async void (event handlers excluded),
  `RIQ1003` `throw ex;` losing the stack trace, `RIQ1004` possible hard-coded secret (the value is never printed).

Sources are parsed in memory: nothing is built, restored or executed, and no file is modified.
Diagnostics that need project references (types, overloads, nullability) are not reported yet.
`review` reads files at the `--head` revision and reports only findings on lines changed since the
merge base with `--base`. RibatIQ reports likely issues; it does not prove that code is correct or secure.

## Exit codes

| Code | Meaning |
|------|---------|
| `0` | Completed; no findings met the configured failure threshold. |
| `1` | Completed; findings met the configured failure threshold. |
| `2` | Invalid usage, configuration, or input. |
| `3` | Operational failure (Git, LLM provider, I/O, ...). Never reported as a clean result. |

## Project layout

| Project | Responsibility |
|---------|----------------|
| `src/RibatIQ.Domain` | Findings, severities, result types. No external dependencies. |
| `src/RibatIQ.Application` | Use cases and ports (LLM provider, Git, discovery, reporting). Depends on Domain only. |
| `src/RibatIQ.Infrastructure` | Adapters: OpenAI/Anthropic, Git, Roslyn, filesystem. |
| `src/RibatIQ.Cli` | The `ribat` command: parsing, DI, console output, exit codes. |
| `tests/RibatIQ.Application.Tests` | Unit tests for settings, globs, diff parsing and the scan/review workflows (with fakes). |
| `tests/RibatIQ.Domain.Tests` | Unit tests for domain validation, fingerprints and result contracts. |
| `tests/RibatIQ.IntegrationTests` | Discovery, Roslyn and Git adapters, end-to-end `scan`/`review`, architecture guards. |
| `tests/Fixtures` | Sample inputs with intentional defects, analyzed (never built) by the tests. |

## License

[MIT](LICENSE)
