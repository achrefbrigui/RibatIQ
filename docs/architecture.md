# Architecture

RibatIQ is a modular monolith: one command-line application split into four projects with a
strict dependency direction. Vendor SDKs, Git and the CLI framework never reach the core.

```text
RibatIQ.Cli  ──►  RibatIQ.Infrastructure  ──►  RibatIQ.Application  ──►  RibatIQ.Domain
(System.CommandLine)   (Roslyn, Git, OpenAI,        (use cases, ports)        (findings, results)
                        Anthropic, JSON)
```

`tests/RibatIQ.IntegrationTests/ArchitectureTests.cs` fails the build if Domain or Application
start referencing the CLI framework, provider SDKs or Infrastructure.

## Projects

| Project | Responsibility | Key types |
|---------|----------------|-----------|
| Domain | Validated findings and results; no dependencies. | `ReviewFinding`, `FindingFingerprint`, `RepositoryPath`, `AnalysisResult`, `FindingSummary` |
| Application | Workflows and the ports they need. | `ScanService`, `ReviewService`, `AiAnalyzer`, `AiFindingParser`, `UnifiedDiffParser`, `SettingsResolver`, `ISourceDiscovery`, `IStaticAnalyzer`, `IGitClient`, `ILlmProvider`, `IReportWriter` |
| Infrastructure | Adapters for the outside world. | `FileSystemSourceDiscovery`, `RoslynStaticAnalyzer`, `GitCli`, `OpenAiProvider`, `AnthropicProvider`, `JsonReportWriter` |
| Cli | Parsing, configuration sources, composition, console output, exit codes. | `RibatCli`, `ScanCommand`, `ReviewCommand`, `AnalysisOptions`, `ConfigFileLoader`, `ReportPublisher` |

## Pipeline

Both commands follow the same steps; only the source of documents differs.

1. **Input validation**: target path or Git revisions (`ScanRequest`, `ReviewRequest`), settings
   from command line, environment and `ribatiq.json` (`SettingsResolver`).
2. **Provider resolution**: when AI is enabled the provider is created first, so a missing API key
   fails before any work.
3. **Discovery**: `scan` walks the solution, project or directory; `review` runs
   `git diff base...head` and reads changed files at the head revision.
4. **Deterministic analysis**: Roslyn parses the files in memory and runs the RibatIQ analyzers.
5. **AI analysis** (optional): bounded context, prompt, provider call.
6. **Validation**: model output is parsed and every finding is checked against what was sent
   (`AiFindingParser`); review findings must touch changed lines (`DiffLocationValidator`).
7. **Normalization**: findings are de-duplicated by fingerprint (static analysis wins over AI).
8. **Reporting**: console or JSON, to standard output or a file.
9. **Exit policy**: the `--fail-on` threshold is applied after the report is written.

## Findings and fingerprints

A `ReviewFinding` is valid by construction: required text, bounded lengths, repository-relative
paths (no absolute paths, no `..`), consistent line ranges, confidence in [0, 1]. Its `Id` is a
versioned SHA-256 fingerprint of the file path, the rule (or normalized title) and the start line,
so the same issue keeps the same ID across runs and providers. This is the basis for a future
baseline feature.

## Error model

| Exception | Meaning | Exit code |
|-----------|---------|-----------|
| `ConfigurationException` | Invalid settings or missing API key. | 2 |
| `InvalidInputException` | Unusable target, solution file or Git revision. | 2 |
| `OperationFailedException` | Git, provider or I/O failure; malformed AI output. | 3 |
| `LlmProviderException` | Provider-level failure, wrapped into `OperationFailedException`. | 3 |

## Adding an LLM provider

1. Implement `ILlmProvider` in `RibatIQ.Infrastructure/Llm`, keeping SDK types internal and mapping
   failures to `LlmErrorKind` (see `ProviderErrors`).
2. Read the API key from one environment variable and throw `ConfigurationException` when it is missing.
3. Register the provider ID in `LlmProviders` and in `LlmProviderRegistry`.
4. Add a subclass of `LlmProviderContractTests` with the provider's wire formats.
