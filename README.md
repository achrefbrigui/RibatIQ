# RibatIQ

**Open-source AI code intelligence for .NET.**
*Built for developers who never stop improving. Guarding code. Growing intelligence.*

> **Status: early development (Milestone 1 — skeleton).**
> Only `ribat --help` and `ribat --version` are implemented. `scan` and `review` are planned and
> do not exist yet.

## Requirements

- .NET SDK 10.0.100 or later (pinned in `global.json`, rolls forward to the latest feature band).

## Build and test

```bash
dotnet build
dotnet test
```

Tests require no API keys and no network access beyond the initial NuGet restore.

## Run

```bash
dotnet run --project src/RibatIQ.Cli -- --help
dotnet run --project src/RibatIQ.Cli -- --version
```

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
| `tests/RibatIQ.IntegrationTests` | End-to-end CLI tests and dependency-direction guards. |

## License

[MIT](LICENSE)
